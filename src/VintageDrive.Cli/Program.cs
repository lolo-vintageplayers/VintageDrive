using System;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using VintageDrive.Core.Capacity;
using VintageDrive.Core.Disks;
using VintageDrive.Core.Format;
using VintageDrive.Core.Presets;
using VintageDrive.Core.Util;
using VintageDrive.Core.Wipe;

namespace VintageDrive.Cli
{
    /// <summary>CLI de développement du moteur VintageDrive (le produit final sera l'appli graphique).</summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            Console.WriteLine();
            Console.WriteLine("  VINTAGEDRIVE — moteur v0.1.0 — © VintagePlayers, licence MIT");
            Console.WriteLine("  Admin : " + (IsElevated() ? "oui" : "non"));
            Console.WriteLine();

            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
            try
            {
                switch (cmd)
                {
                    case "list": return CmdList();
                    case "info": return CmdInfo(args);
                    case "presets": return CmdPresets(args);
                    case "probe": return CmdCapacity(args, quick: true);
                    case "fulltest": return CmdCapacity(args, quick: false);
                    case "format": return CmdFormat(args);
                    case "clean": return CmdClean(args);
                    case "wipe": return CmdWipe(args);
                    default: PrintUsage(); return 2;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("  ERREUR : " + ex.Message);
                return 1;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("  Commandes :");
            Console.WriteLine("    list                                              inventaire des disques (défaut)");
            Console.WriteLine("    info <n°>                                         identité, partitions et volumes du disque");
            Console.WriteLine("    presets                                           liste des réglages consoles");
            Console.WriteLine("    probe <n°> --yes [--force]                        test rapide de capacité réelle (EFFACE TOUT)");
            Console.WriteLine("    fulltest <n°> --yes [--force]                     test complet 100 % surface (long, EFFACE TOUT)");
            Console.WriteLine("    format <n°> [--preset wii | --fs fat32|exfat|ntfs]");
            Console.WriteLine("               [--cluster 32K] [--label NOM] --yes    formatage MBR (EFFACE TOUT)");
            Console.WriteLine("    clean <n°> --yes [--force]                        nettoyage rapide : zéros début+fin (EFFACE TOUT)");
            Console.WriteLine("    wipe <n°> --yes [--force]                         effacement complet à zéro (long, EFFACE TOUT)");
        }

        private static int CmdList()
        {
            var disks = DiskEnumerator.GetDisks();
            if (disks.Count == 0)
            {
                Console.WriteLine("  Aucun disque détecté (?)");
                return 1;
            }
            foreach (var disk in disks)
                PrintDisk(disk);
            Console.WriteLine();
            return 0;
        }

        private static int CmdInfo(string[] args)
        {
            if (args.Length < 2 || !int.TryParse(args[1], out int index))
            {
                Console.WriteLine("  Usage : vintagedrive info <n° de disque>");
                return 2;
            }
            var disk = DiskEnumerator.GetDisks().FirstOrDefault(d => d.Index == index);
            if (disk == null) { Console.WriteLine($"  Disque {index} introuvable."); return 2; }

            var det = DiskInspector.GetDetails(disk);
            Console.WriteLine($"  DISQUE {disk.Index} — {disk.Model}");
            Console.WriteLine($"  Série : {(disk.SerialNumber.Length > 0 ? disk.SerialNumber : "—")} · firmware : {(disk.FirmwareRevision.Length > 0 ? disk.FirmwareRevision : "—")}");
            Console.WriteLine($"  Bus {disk.Bus} · {(disk.IsRemovableMedia ? "amovible" : "fixe")} · secteurs {disk.BytesPerSector} o · table {det.PartitionStyle}");
            Console.WriteLine($"  Taille : {disk.SizeBytes:N0} octets = {ByteFormatter.Decimal(disk.SizeBytes)} (vendeur) = {ByteFormatter.Binary(disk.SizeBytes)} (Windows)");
            Console.WriteLine();
            Console.WriteLine($"  Partitions ({det.Partitions.Count}) :");
            foreach (var p in det.Partitions)
                Console.WriteLine($"    n°{p.Number}  {p.TypeName,-22} {ByteFormatter.Decimal(p.LengthBytes),10}  début {ByteFormatter.Decimal(p.OffsetBytes)}"
                    + (p.Letter.Length > 0 ? $"  → {p.Letter}" : "  (non montée)")
                    + (p.IsBootFlagged ? "  [amorçable]" : "")
                    + (p.GptName.Length > 0 ? $"  « {p.GptName} »" : ""));
            if (det.Partitions.Count > 1)
                Console.WriteLine("    ATTENTION : plusieurs partitions — beaucoup de consoles/loaders ne lisent que la première !");
            if (det.Partitions.Count == 0)
                Console.WriteLine("    aucune — support RAW, à formater");
            foreach (var v in det.Volumes)
            {
                Console.WriteLine();
                long used = v.TotalBytes - v.FreeBytes;
                Console.WriteLine($"  Volume {v.Letter} « {v.Label} » — {v.FileSystem}"
                    + (v.ClusterBytes > 0 ? $" · clusters {v.ClusterBytes >> 10} Ko" : "")
                    + (v.SerialHex.Length > 0 ? $" · série {v.SerialHex}" : ""));
                if (v.TotalBytes > 0)
                    Console.WriteLine($"    {ByteFormatter.Decimal(used)} utilisés · {ByteFormatter.Decimal(v.FreeBytes)} libres ({(double)used / v.TotalBytes * 100:F0} % plein)");
            }
            Console.WriteLine();
            return 0;
        }

        private static int CmdPresets(string[] args)
        {
            if (args.Length > 1 && !args[1].StartsWith("-"))
            {
                var preset = ConsolePresets.Find(args[1]);
                if (preset == null)
                {
                    Console.WriteLine($"  Preset inconnu : « {args[1]} » — tape « vintagedrive presets » pour la liste.");
                    return 2;
                }
                PrintPresetCard(preset);
                return 0;
            }

            Console.WriteLine("  Presets consoles :");
            string? category = null;
            foreach (var p in ConsolePresets.All)
            {
                if (p.Category != category)
                {
                    category = p.Category;
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine($"   ── {category} ──");
                    Console.ResetColor();
                }
                if (p.CanFormat)
                {
                    string cluster = p.ClusterBytes > 0 ? $"{p.ClusterBytes >> 10} Kio" : "auto";
                    Console.WriteLine($"    {p.Key,-10} {FsName(p.Fs),-6} {cluster,-7} MBR   {p.Name}"
                        + (p.Notes.Length > 0 ? $"  — {p.Notes}" : ""));
                }
                else
                {
                    Console.WriteLine($"    {p.Key,-10} {"ⓘ info",-19}     {p.Name}"
                        + (p.Notes.Length > 0 ? $"  — {p.Notes}" : ""));
                }
            }
            Console.WriteLine();
            Console.WriteLine("  Explication pédagogique : vintagedrive presets <clé>");
            Console.WriteLine("  Formater : vintagedrive format <n°> --preset <clé> --yes");
            return 0;
        }

        private static void PrintPresetCard(ConsolePreset p)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  ▌ {p.Name}");
            Console.ResetColor();
            if (p.CanFormat)
            {
                string cluster = p.ClusterBytes > 0 ? $"clusters {p.ClusterBytes >> 10} Kio" : "clusters automatiques";
                Console.WriteLine($"  ▌ {FsName(p.Fs)} · {cluster} · table MBR");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  ▌ ⓘ Pas de formatage PC pour ce cas — voilà pourquoi :");
                Console.ResetColor();
            }
            Console.WriteLine();
            PrintWrapped(p.Pedagogy, "  ");
            Console.WriteLine();
        }

        private static string FsName(TargetFs fs)
            => fs == TargetFs.Fat32 ? "FAT32" : fs == TargetFs.ExFat ? "exFAT" : "NTFS";

        private static void PrintWrapped(string text, string indent, int width = 84)
        {
            var line = new StringBuilder();
            foreach (string word in text.Split(' '))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > width)
                {
                    Console.WriteLine(indent + line);
                    line.Clear();
                }
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
            if (line.Length > 0) Console.WriteLine(indent + line);
        }

        /// <summary>Parse l'index cible et applique tous les garde-fous communs aux opérations destructives.</summary>
        private static PhysicalDisk? ResolveGuardedTarget(string[] args, string usage, out int failCode)
        {
            failCode = 2;
            if (args.Length < 2 || !int.TryParse(args[1], out int index))
            {
                Console.WriteLine("  Usage : " + usage);
                return null;
            }
            bool yes = Array.IndexOf(args, "--yes") >= 0;
            bool force = Array.IndexOf(args, "--force") >= 0;

            var disk = DiskEnumerator.GetDisks().FirstOrDefault(d => d.Index == index);
            if (disk == null) { Console.WriteLine($"  Disque {index} introuvable."); return null; }

            Console.WriteLine($"  Cible : DISQUE {disk.Index} — {disk.Model} — {ByteFormatter.Decimal(disk.SizeBytes)} — bus {disk.Bus}");
            Console.WriteLine();

            failCode = 3;
            if (disk.IsSystemDisk) { Console.WriteLine("  REFUS : c'est le disque système."); return null; }
            foreach (string p in new[] { AppDomain.CurrentDomain.BaseDirectory, Environment.CurrentDirectory })
            {
                if (p != null && DiskEnumerator.GetDiskNumberForPath(p) == disk.Index)
                { Console.WriteLine("  REFUS : ce disque héberge le programme ou le dossier courant."); return null; }
            }
            bool removable = disk.Bus == StorageBus.Usb || disk.Bus == StorageBus.Sd
                          || disk.Bus == StorageBus.Mmc || disk.IsRemovableMedia;
            if (!removable && !force)
            { Console.WriteLine("  REFUS : disque interne (ni USB, ni SD). Ajoute --force si tu es absolument sûr de toi."); return null; }
            if (!IsElevated())
            { Console.WriteLine("  REFUS : droits administrateur requis pour écrire sur le disque brut."); return null; }
            if (!yes)
            {
                Console.WriteLine("  ATTENTION : opération DESTRUCTIVE — toutes les données du disque seront effacées, partitions comprises.");
                Console.WriteLine("  Ajoute --yes pour confirmer.");
                return null;
            }
            return disk;
        }

        private static int CmdCapacity(string[] args, bool quick)
        {
            string name = quick ? "probe" : "fulltest";
            var disk = ResolveGuardedTarget(args, $"vintagedrive {name} <n° de disque> --yes [--force]", out int rc);
            if (disk == null) return rc;

            Action<string> log = s => Console.WriteLine("  " + s);

            if (quick)
            {
                var r = CapacityProbe.Run(disk, log, CancellationToken.None);
                PrintProbe(r);
                return r.Verdict == CapacityVerdict.Conforme ? 0 : 4;
            }
            else
            {
                var r = FullSurfaceTest.Run(disk, log, CancellationToken.None);
                PrintFull(r);
                return r.Conforme ? 0 : 4;
            }
        }

        private static int CmdFormat(string[] args)
        {
            var disk = ResolveGuardedTarget(args,
                "vintagedrive format <n°> [--preset wii | --fs fat32|exfat|ntfs] [--cluster 32K] [--label NOM] --yes [--force]",
                out int rc);
            if (disk == null) return rc;

            ConsolePreset? preset = null;
            string? presetKey = GetArgValue(args, "--preset");
            if (presetKey != null)
            {
                preset = ConsolePresets.Find(presetKey);
                if (preset == null)
                {
                    Console.WriteLine($"  Preset inconnu : « {presetKey} » — liste disponible via : vintagedrive presets");
                    return 2;
                }
                if (!preset.CanFormat)
                {
                    PrintPresetCard(preset);
                    return 3;
                }
                Console.WriteLine($"  Preset : {preset.Name}");
            }

            TargetFs fs = preset != null ? preset.Fs : TargetFs.Fat32;
            string? fsArg = GetArgValue(args, "--fs");
            if (fsArg != null)
            {
                switch (fsArg.ToLowerInvariant())
                {
                    case "fat32": fs = TargetFs.Fat32; break;
                    case "exfat": fs = TargetFs.ExFat; break;
                    case "ntfs": fs = TargetFs.Ntfs; break;
                    default:
                        Console.WriteLine($"  Système de fichiers incompris : « {fsArg} » (attendu : fat32, exfat ou ntfs)");
                        return 2;
                }
            }

            int cluster = preset != null ? preset.ClusterBytes : (fs == TargetFs.Fat32 ? 32 << 10 : 0);
            string? cl = GetArgValue(args, "--cluster");
            if (cl != null)
            {
                cluster = ParseCluster(cl);
                if (cluster == 0)
                {
                    Console.WriteLine($"  Taille de cluster incomprise : « {cl} » (attendu : 4K, 8K, 16K, 32K, 64K…)");
                    return 2;
                }
            }
            string label = GetArgValue(args, "--label") ?? "";

            Action<string> log = s => Console.WriteLine("  " + s);

            if (fs == TargetFs.Fat32)
            {
                var opt = new Fat32FormatOptions { ClusterBytes = cluster, Label = label };
                var report = Fat32Formatter.FormatDisk(disk, opt, log, CancellationToken.None);
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  FORMATAGE FAT32 TERMINÉ ✔ en {report.Duration.TotalSeconds:F1} s");
                Console.ResetColor();
                Console.WriteLine($"  Partition MBR : départ {report.PartitionOffsetBytes >> 20} Mio · {ByteFormatter.Decimal(report.PartitionBytes)}");
                Console.WriteLine($"  FAT32 : clusters de {report.ClusterBytes >> 10} Kio · {report.ClusterCount:N0} clusters · 2 FAT de {report.FatSectors * disk.BytesPerSector >> 20} Mio");
            }
            else
            {
                var duration = WindowsFormatter.PrepareAndFormatDisk(disk, fs, label, cluster, log, CancellationToken.None);
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  FORMATAGE {(fs == TargetFs.ExFat ? "exFAT" : "NTFS")} TERMINÉ ✔ en {duration.TotalSeconds:F1} s");
                Console.ResetColor();
            }

            return WaitAndShowVolume(disk.Index);
        }

        private static int CmdClean(string[] args)
        {
            var disk = ResolveGuardedTarget(args, "vintagedrive clean <n°> --yes [--force]", out int rc);
            if (disk == null) return rc;

            var r = Wiper.QuickClean(disk, s => Console.WriteLine("  " + s), CancellationToken.None);
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  NETTOYAGE TERMINÉ ✔ en {r.Duration.TotalSeconds:F1} s");
            Console.ResetColor();
            Console.WriteLine("  Le disque est vierge (RAW) : formate-le avant usage.");
            return 0;
        }

        private static int CmdWipe(string[] args)
        {
            var disk = ResolveGuardedTarget(args, "vintagedrive wipe <n°> --yes [--force]", out int rc);
            if (disk == null) return rc;

            Console.WriteLine("  Effacement complet : la durée dépend du support (taille ÷ vitesse d'écriture).");
            var r = Wiper.FullWipe(disk, s => Console.WriteLine("  " + s), CancellationToken.None);
            Console.WriteLine();
            Console.ForegroundColor = r.WriteErrors > 0 ? ConsoleColor.Yellow : ConsoleColor.Green;
            Console.WriteLine($"  EFFACEMENT TERMINÉ {(r.WriteErrors > 0 ? "avec erreurs" : "✔")} : {ByteFormatter.Decimal(r.BytesWritten)} à zéro en {r.Duration.TotalMinutes:F0} min ({r.AvgWriteMBps:F1} Mo/s)");
            Console.ResetColor();
            if (r.WriteErrors > 0)
                Console.WriteLine($"  {r.WriteErrors} erreurs d'écriture : support probablement en fin de vie.");
            Console.WriteLine("  Le disque est vierge (RAW) : formate-le avant usage.");
            return r.WriteErrors > 0 ? 4 : 0;
        }

        private static int WaitAndShowVolume(int diskIndex)
        {
            Console.WriteLine("  Attente du montage Windows…");
            for (int i = 0; i < 12; i++)
            {
                Thread.Sleep(500);
                var again = DiskEnumerator.GetDisks().FirstOrDefault(d => d.Index == diskIndex);
                if (again != null && again.Volumes.Count > 0 && again.Volumes[0].IsReady)
                {
                    Console.WriteLine();
                    PrintDisk(again);
                    return 0;
                }
            }
            Console.WriteLine("  Volume pas encore monté — débranche et rebranche le support si besoin.");
            return 0;
        }

        private static string? GetArgValue(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static int ParseCluster(string s)
        {
            s = s.Trim().ToUpperInvariant();
            int mult = 1;
            if (s.EndsWith("KO")) { mult = 1024; s = s.Substring(0, s.Length - 2); }
            else if (s.EndsWith("K")) { mult = 1024; s = s.Substring(0, s.Length - 1); }
            return int.TryParse(s, out int v) && v > 0 ? v * mult : 0;
        }

        private static void PrintProbe(ProbeResult r)
        {
            Console.WriteLine();
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            switch (r.Verdict)
            {
                case CapacityVerdict.Conforme:
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  VERDICT : CAPACITÉ CONFORME ✔");
                    Console.ResetColor();
                    Console.WriteLine($"  Annoncé : {ByteFormatter.Decimal(r.ClaimedBytes)} — aucun signe de falsification");
                    Console.WriteLine($"  Échantillon : {r.PointsTotal} blocs de {r.BlockBytes >> 10} Kio répartis sur 100 % de la plage (un tous les {r.GridStepBytes >> 20} Mio)");
                    break;

                case CapacityVerdict.FakeWrap:
                case CapacityVerdict.FakeDiscard:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  VERDICT : ✖ CAPACITÉ FALSIFIÉE — GAME OVER, INSERT REAL DRIVE");
                    Console.ResetColor();
                    Console.WriteLine($"  Annoncé : {ByteFormatter.Decimal(r.ClaimedBytes)}");
                    Console.WriteLine($"  Réel estimé : {ByteFormatter.Decimal(r.EstimatedRealBytes)}"
                        + (r.Refined ? " (affiné par dichotomie)"
                                     : $" (entre {ByteFormatter.Decimal(r.EstimateLowBytes)} et {ByteFormatter.Decimal(r.EstimateHighBytes)})"));
                    Console.WriteLine("  Type : " + (r.Verdict == CapacityVerdict.FakeWrap
                        ? "contrôleur qui boucle — les écritures hautes écrasent les données basses"
                        : "les écritures au-delà de la puce réelle sont silencieusement jetées"));
                    Console.WriteLine($"  Détail : {r.PointsOk} points intacts · {r.PointsForeign} écrasés · {r.PointsGarbage} perdus · {r.PointsIoError} erreurs E/S");
                    Console.WriteLine($"  → Tout fichier écrit au-delà de {ByteFormatter.Decimal(r.EstimatedRealBytes)} serait CORROMPU.");
                    break;

                case CapacityVerdict.Incoherent:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("  VERDICT : RÉSULTATS INCOHÉRENTS — support instable");
                    Console.ResetColor();
                    Console.WriteLine($"  {r.PointsOk} intacts · {r.PointsForeign} écrasés · {r.PointsGarbage} perdus · {r.PointsIoError} erreurs E/S");
                    Console.WriteLine("  Lance le test complet (fulltest) pour un diagnostic définitif.");
                    break;

                case CapacityVerdict.Defaillant:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  VERDICT : SUPPORT DÉFAILLANT — trop d'erreurs d'entrée/sortie");
                    Console.ResetColor();
                    Console.WriteLine($"  {r.PointsIoError} erreurs E/S sur {r.PointsTotal} points : ce support est en train de mourir.");
                    break;
            }
            if (r.SeqWriteMBps > 0)
                Console.WriteLine($"  Vitesse : séquentiel {r.SeqWriteMBps:F1} Mo/s écriture · {r.SeqReadMBps:F1} Mo/s lecture · aléatoire {r.BlockBytes >> 10} Kio {r.ScatterWriteMBps:F1} Mo/s");
            Console.WriteLine($"  Durée : {r.Duration.TotalSeconds:F0} s");
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            Console.WriteLine("  (Le support n'a plus de partition : reformate-le avant usage.)");
            Console.WriteLine();
        }

        private static void PrintFull(FullTestResult r)
        {
            Console.WriteLine();
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            if (r.Conforme)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  VERDICT : CAPACITÉ CONFORME ✔ (100 % de la surface vérifiée)");
                Console.ResetColor();
                Console.WriteLine($"  {ByteFormatter.Decimal(r.GoodBytes)} vérifiés sur {ByteFormatter.Decimal(r.ClaimedBytes)} annoncés");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  VERDICT : ✖ SUPPORT NON CONFORME");
                Console.ResetColor();
                Console.WriteLine($"  Annoncé : {ByteFormatter.Decimal(r.ClaimedBytes)} — réellement utilisable : {ByteFormatter.Decimal(r.GoodBytes)}");
                if (r.FirstMismatch >= 0)
                    Console.WriteLine($"  Première corruption à l'offset {ByteFormatter.Decimal(r.FirstMismatch)}");
            }
            if (r.WriteErrors + r.ReadErrors > 0)
                Console.WriteLine($"  Erreurs E/S : {r.WriteErrors} en écriture · {r.ReadErrors} en lecture");
            Console.WriteLine($"  Vitesse moyenne : {r.AvgWriteMBps:F1} Mo/s écriture · {r.AvgReadMBps:F1} Mo/s lecture");
            Console.WriteLine($"  Durée : {r.Duration.TotalMinutes:F0} min");
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            Console.WriteLine("  (Le support n'a plus de partition : reformate-le avant usage.)");
            Console.WriteLine();
        }

        private static void PrintDisk(PhysicalDisk d)
        {
            Console.ForegroundColor = d.IsSystemDisk ? ConsoleColor.Red
                                    : d.Bus == StorageBus.Usb || d.Bus == StorageBus.Sd ? ConsoleColor.Green
                                    : ConsoleColor.Cyan;
            Console.Write($"  DISQUE {d.Index}");
            Console.ResetColor();

            Console.Write($"  {ByteFormatter.Decimal(d.SizeBytes),10}  {BusName(d.Bus),-5}  {StyleName(d.PartitionStyle),-4}  {d.Model}");
            if (d.IsSystemDisk)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("  [DISQUE SYSTÈME — intouchable]");
                Console.ResetColor();
            }
            if (d.IsRemovableMedia) Console.Write("  [amovible]");
            Console.WriteLine();

            foreach (var v in d.Volumes)
            {
                string fs = v.IsReady ? v.FileSystem : "RAW / non formaté";
                string label = v.IsReady && v.Label.Length > 0 ? $"  « {v.Label} »" : "";
                string size = v.IsReady
                    ? $"  {ByteFormatter.Decimal(v.TotalBytes)} ({ByteFormatter.Decimal(v.FreeBytes)} libres)"
                    : "";
                Console.WriteLine($"      {v.Letter}  {fs,-18}{label}{size}");
            }
        }

        private static string BusName(StorageBus bus) => bus switch
        {
            StorageBus.Usb => "USB",
            StorageBus.Sata => "SATA",
            StorageBus.Nvme => "NVMe",
            StorageBus.Sd => "SD",
            StorageBus.Mmc => "MMC",
            StorageBus.Ata => "ATA",
            StorageBus.Atapi => "ATAPI",
            StorageBus.Scsi => "SCSI",
            StorageBus.Raid => "RAID",
            StorageBus.Virtual or StorageBus.FileBackedVirtual => "VIRT",
            StorageBus.Spaces => "SPACE",
            _ => bus.ToString().ToUpperInvariant(),
        };

        private static string StyleName(PartStyle s) => s switch
        {
            PartStyle.Mbr => "MBR",
            PartStyle.Gpt => "GPT",
            _ => "RAW",
        };

        private static bool IsElevated()
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
