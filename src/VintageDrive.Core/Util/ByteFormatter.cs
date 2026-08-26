using System;
using System.Globalization;

namespace VintageDrive.Core.Util
{
    /// <summary>
    /// Formatage des tailles. Unités décimales (Go = 10^9 octets) par défaut : c'est ce qui est
    /// imprimé sur les produits — indispensable pour comparer « annoncé » vs « réel ».
    /// </summary>
    public static class ByteFormatter
    {
        private static readonly string[] DecimalUnits = { "o", "Ko", "Mo", "Go", "To", "Po" };
        private static readonly string[] BinaryUnits = { "o", "Kio", "Mio", "Gio", "Tio", "Pio" };

        /// <summary>Unités binaires (Gio = 2^30) — ce que Windows affiche, d'où l'éternel « il manque des Go ».</summary>
        public static string Binary(long bytes, CultureInfo? culture = null)
        {
            culture ??= CultureInfo.CurrentCulture;
            double value = bytes;
            int unit = 0;
            while (Math.Abs(value) >= 1024 && unit < BinaryUnits.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return value.ToString(unit == 0 ? "0" : "0.#", culture) + " " + BinaryUnits[unit];
        }

        public static string Decimal(long bytes, CultureInfo? culture = null)
        {
            culture ??= CultureInfo.CurrentCulture;
            double value = bytes;
            int unit = 0;
            while (Math.Abs(value) >= 1000 && unit < DecimalUnits.Length - 1)
            {
                value /= 1000;
                unit++;
            }
            return value.ToString(unit == 0 ? "0" : "0.#", culture) + " " + DecimalUnits[unit];
        }
    }
}
