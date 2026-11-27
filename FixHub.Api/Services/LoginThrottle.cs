namespace FixHub.Api.Services;

// Защита входа от перебора пароля: после нескольких неверных попыток подряд вход для этого email
// закрывается на некоторое время. Счётчик хранится в памяти приложения и сбрасывается при его перезапуске.
public interface ILoginThrottle
{
    // Сколько ещё ждать, если вход закрыт; null, если пробовать можно
    TimeSpan? GetLockRemaining(string key);

    void RegisterFailure(string key);

    void Reset(string key);
}

// Настройки (раздел "Security"): MaxLoginAttempts (по умолчанию 5), LockoutMinutes (по умолчанию 15)
public class LoginThrottle : ILoginThrottle
{
    private sealed class Entry
    {
        public int Failures;
        public DateTime LastFailure;
        public DateTime? LockedUntil;
    }

    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly int _maxAttempts;
    private readonly TimeSpan _lockout;

    public LoginThrottle(IConfiguration config)
    {
        _maxAttempts = Math.Max(1, config.GetValue("Security:MaxLoginAttempts", 5));
        _lockout = TimeSpan.FromMinutes(Math.Max(1, config.GetValue("Security:LockoutMinutes", 15)));
    }

    public TimeSpan? GetLockRemaining(string key)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out var entry) || entry.LockedUntil == null)
                return null;

            var left = entry.LockedUntil.Value - DateTime.UtcNow;
            if (left > TimeSpan.Zero)
                return left;

            // время блокировки вышло: начинаем с чистого листа
            _entries.Remove(key);
            return null;
        }
    }

    public void RegisterFailure(string key)
    {
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            Prune(now);

            if (!_entries.TryGetValue(key, out var entry) || now - entry.LastFailure > _lockout)
            {
                entry = new Entry();
                _entries[key] = entry;
            }

            entry.Failures++;
            entry.LastFailure = now;
            if (entry.Failures >= _maxAttempts)
                entry.LockedUntil = now + _lockout;
        }
    }

    public void Reset(string key)
    {
        lock (_sync)
        {
            _entries.Remove(key);
        }
    }

    // чтобы память не росла от случайных адресов, устаревшие записи удаляем
    private void Prune(DateTime now)
    {
        if (_entries.Count < 1000)
            return;

        foreach (var key in _entries.Where(e => now - e.Value.LastFailure > _lockout).Select(e => e.Key).ToList())
            _entries.Remove(key);
    }
}
