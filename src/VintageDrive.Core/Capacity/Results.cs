using System;

namespace VintageDrive.Core.Capacity
{
    public enum CapacityVerdict
    {
        Conforme,     // aucun signe de falsification
        FakeWrap,     // contrôleur qui boucle : les écritures hautes écrasent les basses
        FakeDiscard,  // les écritures au-delà de la puce réelle sont jetées
        Incoherent,   // résultats non monotones : support instable, test complet recommandé
        Defaillant,   // trop d'erreurs d'E/S : support mourant
    }

    /// <summary>Avancement du test rapide, pour les interfaces graphiques.</summary>
    public sealed class ProbeProgress
    {
        public string Phase { get; internal set; } = "";
        public int Done { get; internal set; }
        public int Total { get; internal set; }
    }

    /// <summary>Résultat du test rapide (échantillonné).</summary>
    public sealed class ProbeResult
    {
        public CapacityVerdict Verdict { get; internal set; }
        public long ClaimedBytes { get; internal set; }
        public long EstimatedRealBytes { get; internal set; }
        public long EstimateLowBytes { get; internal set; }
        public long EstimateHighBytes { get; internal set; }
        public long GridStepBytes { get; internal set; }
        public long BlockBytes { get; internal set; }
        public int PointsTotal { get; internal set; }
        public int PointsOk { get; internal set; }
        public int PointsForeign { get; internal set; }
        public int PointsGarbage { get; internal set; }
        public int PointsIoError { get; internal set; }
        public double ScatterWriteMBps { get; internal set; }
        public double SeqWriteMBps { get; internal set; }
        public double SeqReadMBps { get; internal set; }
        public bool Refined { get; internal set; }
        public TimeSpan Duration { get; internal set; }

        /// <summary>Convertit un résultat de test complet en verdict affichable (PointsTotal = 0 signale « surface 100 % »).</summary>
        public static ProbeResult FromFullSurface(FullTestResult f)
        {
            return new ProbeResult
            {
                Verdict = f.Conforme ? CapacityVerdict.Conforme : CapacityVerdict.FakeDiscard,
                ClaimedBytes = f.ClaimedBytes,
                EstimatedRealBytes = f.GoodBytes,
                EstimateLowBytes = f.GoodBytes,
                EstimateHighBytes = f.GoodBytes,
                SeqWriteMBps = f.AvgWriteMBps,
                SeqReadMBps = f.AvgReadMBps,
                Duration = f.Duration,
                Refined = true,
                PointsTotal = 0,
            };
        }
    }

    /// <summary>Résultat du test complet (100 % de la surface écrite puis relue).</summary>
    public sealed class FullTestResult
    {
        public long ClaimedBytes { get; internal set; }
        public long GoodBytes { get; internal set; }
        public long FirstMismatch { get; internal set; } = -1;
        public int WriteErrors { get; internal set; }
        public int ReadErrors { get; internal set; }
        public double AvgWriteMBps { get; internal set; }
        public double AvgReadMBps { get; internal set; }
        public TimeSpan Duration { get; internal set; }

        public bool Conforme => GoodBytes >= ClaimedBytes - ClaimedBytes / 1000;
    }
}
