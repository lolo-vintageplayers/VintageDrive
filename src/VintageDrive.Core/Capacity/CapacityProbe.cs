using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using VintageDrive.Core.Disks;
using VintageDrive.Core.Native;

namespace VintageDrive.Core.Capacity
{
    /// <summary>
    /// Test rapide de capacité réelle (« détecteur d'arnaque »).
    /// Écrit 24 à 512 blocs signés de 256 Kio (nombre auto-calibré sur la vitesse du support)
    /// répartis sur toute la capacité annoncée, puis relit tout :
    /// un contrôleur falsifié qui « boucle » (les écritures hautes écrasent les basses) ou qui jette
    /// les écritures au-delà de la puce réelle est démasqué en quelques minutes.
    /// Piège déjoué : sur un support qui boucle, écrire PUIS relire immédiatement au même offset
    /// « vérifie » toujours (la lecture boucle aussi) — d'où l'écriture de TOUS les marqueurs d'abord.
    /// DESTRUCTIF : écrase tout, table de partitions comprise.
    /// </summary>
    public static class CapacityProbe
    {
        // Mesuré sur clé USB bon marché (Kingston DT 3.0) : ~4 s PAR écriture dispersée,
        // quelle que soit sa taille → c'est le nombre de points qui coûte, pas les octets.
        // D'où le calibrage : on mesure d'abord, on choisit le nombre de points ensuite.
        private const int BlockSize = 256 << 10;      // 256 Kio par point de test (64 pages signées)
        private const int MinPoints = 32;             // plancher : répartis sur 100 % de la plage, toute capacité sérieusement gonflée tombe à coup sûr
        private const int MaxPoints = 512;            // plafond pour les supports rapides
        private const double WriteBudgetSeconds = 60; // budget de la phase d'écriture
        private const int PagesPerBlock = BlockSize / PageCodec.PageSize;

        private enum BlockKind { Self, Foreign, Garbage, IoError }

        public static ProbeResult Run(PhysicalDisk disk, Action<string>? log, CancellationToken ct, Action<ProbeProgress>? progress = null)
        {
            log ??= _ => { };
            progress ??= _ => { };
            if (disk.IsSystemDisk) throw new InvalidOperationException("Refus : disque système.");
            long size = disk.SizeBytes;
            if (size < 64L << 20) throw new InvalidOperationException("Disque trop petit ou taille inconnue.");

            var result = new ProbeResult { ClaimedBytes = size };
            var chrono = Stopwatch.StartNew();

            ulong seed = (ulong)Guid.NewGuid().GetHashCode() << 32 ^ (ulong)Stopwatch.GetTimestamp();

            log($"Verrouillage des volumes du disque {disk.Index}…");
            using var locker = VolumeLocker.LockDisk(disk);
            // NO_BUFFERING seul : pas de cache Windows, mais pas de synchro forcée par écriture
            // (WRITE_THROUGH mesuré catastrophique sur clé bon marché). Un flush en fin de phase
            // suffit : le volume écrit dépasse n'importe quel cache interne de contrôleur.
            using var handle = DeviceIo.TryOpen(disk.DevicePath,
                NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                NativeMethods.FILE_FLAG_NO_BUFFERING,
                out int err) ?? throw new IOException($"Ouverture de {disk.DevicePath} impossible (code Win32 {err}). Admin requis.");

            using var io = new AlignedBuffer(BlockSize);
            var staging = new byte[BlockSize];

            // ── Calibrage : 6 écritures sacrifiées à des offsets IMPAIRS (jamais relus ; et un
            // bouclage sur masque de bits renvoie un impair sur un impair, donc ça ne pollue
            // jamais la grille, qui n'a que des multiples pairs). FLUSH APRÈS CHACUNE : sans ça,
            // le cache RAM du contrôleur répond « 20 ms » alors que le vrai coût d'un saut peut
            // dépasser plusieurs secondes — et le test devient interminable. Les 2 premières
            // écritures chauffent le cache et sont ignorées.
            double perWrite;
            {
                progress(new ProbeProgress { Phase = "Calibrage du support…", Done = 0, Total = 0 });
                var times = new List<double>();
                for (int j = 0; j < 6; j++)
                {
                    ct.ThrowIfCancellationRequested();
                    long m = size * (2 * j + 1) / 12 / BlockSize;
                    if ((m & 1) == 0) m++;
                    long cal = m * BlockSize;
                    if (cal + BlockSize > size) cal -= 2 * BlockSize;
                    var swOne = Stopwatch.StartNew();
                    BuildBlock(staging, seed, cal);
                    try { DeviceIo.WriteAt(handle, cal, io, staging, BlockSize); }
                    catch (IOException) { }
                    NativeMethods.FlushFileBuffers(handle);
                    swOne.Stop();
                    times.Add(swOne.Elapsed.TotalSeconds);
                }
                perWrite = times.Skip(2).Average();
            }

            // Grille de test : pas en puissance de 2 (les contrôleurs falsifiés bouclent sur des
            // masques de bits d'adresse → les collisions retombent pile sur des points de la
            // grille), élargi jusqu'à tenir dans le budget temps mesuré.
            long step = BlockSize;
            while (size / step > MaxPoints) step <<= 1;
            while (size / step > MinPoints && size / step * perWrite > WriteBudgetSeconds) step <<= 1;
            var offsets = new List<long>();
            for (long off = 0; off + BlockSize <= size; off += step) offsets.Add(off);
            long endOff = (size - BlockSize) / BlockSize * BlockSize;
            if (offsets.Count == 0 || offsets[offsets.Count - 1] != endOff) offsets.Add(endOff);
            result.GridStepBytes = step;
            result.BlockBytes = BlockSize;
            result.PointsTotal = offsets.Count;
            log($"Calibrage express : {perWrite * 1000:F0} ms par écriture (indicatif — vérifié sur le terrain juste après)");

            // ── Calibrage sur le terrain : les 6 premiers points de la grille, écrits en
            // soutenu. C'est LA mesure honnête : un cache de contrôleur ne tient pas six
            // écritures dispersées d'affilée. La grille définitive est décidée ICI, une seule
            // fois — le nombre de points affiché ne bouge plus ensuite.
            const double WritePhaseCapSeconds = 75;
            const int FieldCal = 6;
            var swWrite = Stopwatch.StartNew();
            var written = new List<long>();
            int i1 = 0;
            double timeAtTwo = 0;
            while (i1 < offsets.Count && written.Count < FieldCal)
            {
                ct.ThrowIfCancellationRequested();
                long off = offsets[i1];
                BuildBlock(staging, seed, off);
                try { DeviceIo.WriteAt(handle, off, io, staging, BlockSize); }
                catch (IOException) { result.PointsIoError++; }
                written.Add(off);
                if (written.Count == 2) timeAtTwo = swWrite.Elapsed.TotalSeconds;
                // pas de compteur pendant le calibrage : le nombre de points n'est pas encore décidé
                progress(new ProbeProgress { Phase = "Calibrage — mesure de la vraie vitesse du support…", Done = 0, Total = 0 });
                i1++;
            }
            double fieldPer = written.Count > 2
                ? (swWrite.Elapsed.TotalSeconds - timeAtTwo) / (written.Count - 2)
                : Math.Max(perWrite, 0.001);

            // décision UNIQUE du maillage final
            while (offsets.Count - i1 > 8
                   && written.Count + (offsets.Count - i1) / 2 >= MinPoints
                   && swWrite.Elapsed.TotalSeconds + fieldPer * (offsets.Count - i1) > WritePhaseCapSeconds)
            {
                var keep = new List<long>(offsets.Take(i1));
                for (int j = i1 + 1; j < offsets.Count; j += 2) keep.Add(offsets[j]);
                if (keep[keep.Count - 1] != endOff && endOff > keep[keep.Count - 1]) keep.Add(endOff);
                offsets = keep;
                step <<= 1;
            }
            int totalPoints = written.Count + (offsets.Count - i1);
            log($"Calibrage réel : {fieldPer * 1000:F0} ms par écriture soutenue → {totalPoints} points, répartis sur 100 % de la plage");
            log($"Durée estimée : {FormatDuration(swWrite.Elapsed.TotalSeconds + fieldPer * (offsets.Count - i1) + 25)} — dépend de la vitesse du support, pas de sa taille");

            // ── Phase 1 : écriture des marqueurs restants — total STABLE
            log($"Phase 1/2 : écriture de {totalPoints} marqueurs de {BlockSize >> 10} Kio…");
            bool revised = false;
            while (i1 < offsets.Count)
            {
                ct.ThrowIfCancellationRequested();
                long off = offsets[i1];
                BuildBlock(staging, seed, off);
                try { DeviceIo.WriteAt(handle, off, io, staging, BlockSize); }
                catch (IOException) { result.PointsIoError++; }
                written.Add(off);
                progress(new ProbeProgress { Phase = "Écriture des marqueurs signés (1/2)", Done = written.Count, Total = totalPoints });
                if (written.Count % 128 == 0) log($"  écrit {written.Count}/{totalPoints}…");

                // filet de sécurité unique, seulement si le support s'effondre encore (très rare)
                if (!revised && written.Count == totalPoints / 2 && written.Count >= 8)
                {
                    double per2 = swWrite.Elapsed.TotalSeconds / written.Count;
                    int rem = offsets.Count - i1 - 1;
                    if (swWrite.Elapsed.TotalSeconds + per2 * rem > WritePhaseCapSeconds * 2
                        && written.Count + rem / 2 >= MinPoints)
                    {
                        var keep = new List<long>(offsets.Take(i1 + 1));
                        for (int j = i1 + 2; j < offsets.Count; j += 2) keep.Add(offsets[j]);
                        if (keep[keep.Count - 1] != endOff && endOff > keep[keep.Count - 1]) keep.Add(endOff);
                        offsets = keep;
                        step <<= 1;
                        revised = true;
                        totalPoints = written.Count + (offsets.Count - i1 - 1);
                        log($"Le support ralentit encore → maillage réduit une dernière fois : {totalPoints} points (toujours 100 % de la plage couverte)");
                    }
                }
                i1++;
            }
            swWrite.Stop();
            result.ScatterWriteMBps = written.Count * (BlockSize / 1e6) / Math.Max(0.001, swWrite.Elapsed.TotalSeconds);
            NativeMethods.FlushFileBuffers(handle);
            offsets = written;
            result.PointsTotal = offsets.Count;
            result.GridStepBytes = step;

            // ── Phase 2 : relecture et classement de chaque point
            log("Phase 2/2 : relecture et vérification…");
            var kinds = new BlockKind[offsets.Count];
            for (int i = 0; i < offsets.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                kinds[i] = ClassifyAt(handle, io, staging, seed, offsets[i], result);
                progress(new ProbeProgress { Phase = "Relecture et vérification (2/2)", Done = i + 1, Total = offsets.Count });
                if ((i + 1) % 128 == 0) log($"  vérifié {i + 1}/{offsets.Count}…");
            }

            Analyze(result, offsets, kinds);

            // ── Frontière nette (« discard ») : affinage par dichotomie au Mio près
            if (result.Verdict == CapacityVerdict.FakeDiscard)
                Refine(handle, io, staging, result, log, ct);

            // ── Vitesse séquentielle mesurée dans la zone prouvée réelle
            progress(new ProbeProgress { Phase = "Mesure de vitesse séquentielle…", Done = 0, Total = 0 });
            SpeedBurst(handle, result, log, ct);

            result.Duration = chrono.Elapsed;
            return result;
        }

        private static string FormatDuration(double seconds)
            => seconds < 90 ? $"≈ {seconds:F0} s" : $"≈ {Math.Ceiling(seconds / 60):F0} min";

        private static void BuildBlock(byte[] staging, ulong seed, long blockOffset)
        {
            for (int p = 0; p < PagesPerBlock; p++)
                PageCodec.Build(staging, p * PageCodec.PageSize, seed, blockOffset, p);
        }

        private static BlockKind ClassifyAt(SafeFileHandle handle, AlignedBuffer io, byte[] staging, ulong seed, long off, ProbeResult r)
        {
            try { DeviceIo.ReadAt(handle, off, io, staging, BlockSize); }
            catch (IOException) { r.PointsIoError++; return BlockKind.IoError; }

            int self = 0, foreign = 0;
            for (int p = 0; p < PagesPerBlock; p++)
            {
                var kind = PageCodec.Inspect(staging, p * PageCodec.PageSize, seed, off, p, out _);
                if (kind == PageCodec.PageKind.Self) self++;
                else if (kind == PageCodec.PageKind.Foreign) foreign++;
            }
            if (self == PagesPerBlock) { r.PointsOk++; return BlockKind.Self; }
            if (foreign > PagesPerBlock / 2) { r.PointsForeign++; return BlockKind.Foreign; }
            r.PointsGarbage++;
            return BlockKind.Garbage;
        }

        private static void Analyze(ProbeResult r, List<long> offsets, BlockKind[] kinds)
        {
            if (r.PointsIoError > r.PointsTotal / 10)
            {
                r.Verdict = CapacityVerdict.Defaillant;
                r.EstimatedRealBytes = r.GridStepBytes * r.PointsOk;
                r.EstimateLowBytes = 0;
                r.EstimateHighBytes = r.ClaimedBytes;
                return;
            }

            if (r.PointsForeign == 0 && r.PointsGarbage == 0)
            {
                r.Verdict = CapacityVerdict.Conforme;
                r.EstimatedRealBytes = r.EstimateLowBytes = r.EstimateHighBytes = r.ClaimedBytes;
                return;
            }

            if (r.PointsForeign > 0)
            {
                // Bouclage : chaque écriture haute a écrasé un point bas. La proportion de
                // points restés intacts approxime la part réelle de la capacité annoncée
                // (robuste même si le maillage a été élargi en cours de route).
                r.Verdict = CapacityVerdict.FakeWrap;
                long est = (long)((double)r.ClaimedBytes * r.PointsOk / Math.Max(1, r.PointsTotal));
                r.EstimatedRealBytes = Math.Max(BlockSize, est);
                r.EstimateLowBytes = r.EstimatedRealBytes;
                r.EstimateHighBytes = Math.Min(r.ClaimedBytes,
                    (long)((double)r.ClaimedBytes * (r.PointsOk + r.PointsForeign) / Math.Max(1, r.PointsTotal)));
                return;
            }

            // Que du garbage : frontière nette (discard) si tout est bon avant, tout est mauvais après.
            int firstBad = Array.FindIndex(kinds, k => k != BlockKind.Self);
            bool monotonic = true;
            for (int i = firstBad; i >= 0 && i < kinds.Length; i++)
                if (kinds[i] == BlockKind.Self) { monotonic = false; break; }

            if (!monotonic)
            {
                r.Verdict = CapacityVerdict.Incoherent;
                r.EstimatedRealBytes = r.GridStepBytes * r.PointsOk;
                r.EstimateLowBytes = 0;
                r.EstimateHighBytes = r.ClaimedBytes;
                return;
            }

            r.Verdict = CapacityVerdict.FakeDiscard;
            r.EstimateLowBytes = firstBad > 0 ? offsets[firstBad - 1] + BlockSize : 0;
            r.EstimateHighBytes = offsets[firstBad] + BlockSize;
            r.EstimatedRealBytes = r.EstimateLowBytes;
        }

        private static void Refine(SafeFileHandle handle, AlignedBuffer io, byte[] staging, ProbeResult r, Action<string> log, CancellationToken ct)
        {
            log("Dichotomie sur la frontière réelle…");
            ulong seed2 = Prng.Mix(0xC0FFEEUL ^ (ulong)Stopwatch.GetTimestamp());
            long lo = r.EstimateLowBytes, hi = r.EstimateHighBytes;

            for (int iter = 0; iter < 48 && hi - lo > BlockSize; iter++)
            {
                ct.ThrowIfCancellationRequested();
                long mid = lo + (hi - lo) / 2;
                mid -= mid % BlockSize;
                if (mid <= lo) mid = lo + BlockSize;
                if (mid >= hi) break;

                bool persisted = false;
                try
                {
                    BuildBlock(staging, seed2, mid);
                    DeviceIo.WriteAt(handle, mid, io, staging, BlockSize);
                    if (lo >= 8L * BlockSize)
                    {
                        // brouilleur anti-cache : on écrit ailleurs (zone prouvée réelle)
                        // pour vider un éventuel tampon RAM du contrôleur avant relecture
                        for (int d = 1; d <= 16 && (long)d * BlockSize + BlockSize <= lo; d++)
                        {
                            BuildBlock(staging, seed2, d * (long)BlockSize);
                            DeviceIo.WriteAt(handle, d * (long)BlockSize, io, staging, BlockSize);
                        }
                    }
                    NativeMethods.FlushFileBuffers(handle);

                    DeviceIo.ReadAt(handle, mid, io, staging, BlockSize);
                    int selfPages = 0;
                    for (int p = 0; p < PagesPerBlock; p++)
                        if (PageCodec.Inspect(staging, p * PageCodec.PageSize, seed2, mid, p, out _) == PageCodec.PageKind.Self)
                            selfPages++;
                    persisted = selfPages == PagesPerBlock;
                }
                catch (IOException) { persisted = false; }

                if (persisted) lo = mid + BlockSize; else hi = mid;
            }

            r.EstimateLowBytes = lo;
            r.EstimateHighBytes = hi;
            r.EstimatedRealBytes = lo;
            r.Refined = true;
        }

        private static void SpeedBurst(SafeFileHandle handle, ProbeResult r, Action<string> log, CancellationToken ct)
        {
            const int Chunk = 8 << 20;   // 8 Mio
            const int Chunks = 16;       // 128 Mio au total
            long zone = r.Verdict == CapacityVerdict.Conforme ? r.ClaimedBytes : r.EstimatedRealBytes;
            if (zone < Chunk * (Chunks + 4L)) return; // pas la place dans la zone sûre

            long baseOff = Math.Min(1L << 30, zone / 2);
            baseOff -= baseOff % Chunk;

            log("Mesure de vitesse séquentielle (128 Mio)…");
            using var io = new AlignedBuffer(Chunk);
            var buf = new byte[Chunk];
            new Random(42).NextBytes(buf);
            try
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < Chunks; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    DeviceIo.WriteAt(handle, baseOff + (long)i * Chunk, io, buf, Chunk);
                }
                NativeMethods.FlushFileBuffers(handle);
                sw.Stop();
                r.SeqWriteMBps = Chunks * (Chunk / 1e6) / sw.Elapsed.TotalSeconds;

                sw.Restart();
                for (int i = 0; i < Chunks; i++)
                    DeviceIo.ReadAt(handle, baseOff + (long)i * Chunk, io, buf, Chunk);
                sw.Stop();
                r.SeqReadMBps = Chunks * (Chunk / 1e6) / sw.Elapsed.TotalSeconds;
            }
            catch (IOException) { /* vitesse non mesurée, tant pis */ }
        }
    }
}
