using System;

namespace PriceCheckerAvalonia.Core.Services
{
    public static class VersionHelper
    {
        // Простая проверка: пытаемся распарсить версии как System.Version и сравнить.
        // Возвращает true, если candidate > current.
        public static bool IsNewerVersion(string? candidate, string? current)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            if (string.IsNullOrWhiteSpace(current)) return true;

            if (TryParseVersion(candidate, out var cV) && TryParseVersion(current, out var curV))
            {
                return cV > curV;
            }

            // Фоллбек: лексикографическое сравнение
            return string.Compare(candidate, current, StringComparison.OrdinalIgnoreCase) > 0;
        }

        private static bool TryParseVersion(string input, out Version version)
        {
            version = new Version(0, 0, 0, 0);
            // Удалим префиксы 'v' или пробелы
            input = input.Trim();
            if (input.StartsWith("v", StringComparison.OrdinalIgnoreCase)) input = input[1..];

            // Оставим только цифры и точки до появления дефиса (pre-release)
            var idx = input.IndexOf('-');
            if (idx >= 0) input = input.Substring(0, idx);

            // Попробуем парсить с 2 или 3 компонентами
            var parts = input.Split('.');
            try
            {
                if (parts.Length == 1)
                {
                    version = new Version(int.Parse(parts[0]), 0);
                    return true;
                }
                if (parts.Length == 2)
                {
                    version = new Version(int.Parse(parts[0]), int.Parse(parts[1]));
                    return true;
                }
                if (parts.Length == 3)
                {
                    version = new Version(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
                    return true;
                }
                // 4+ components
                var nums = new int[Math.Min(4, parts.Length)];
                for (int i = 0; i < nums.Length; i++) nums[i] = int.TryParse(parts[i], out var v) ? v : 0;
                version = new Version(nums[0], nums.Length > 1 ? nums[1] : 0, nums.Length > 2 ? nums[2] : 0, nums.Length > 3 ? nums[3] : 0);
                return true;
            }
            catch
            {
                version = new Version(0, 0, 0, 0);
                return false;
            }
        }
    }
}
