namespace KwartoTV.Core;

public sealed record LineupState(string? CurrentId, string? PreviousId, List<string> Favorites);

/// <summary>Cable-style lineup: numbered channels, up/down surfing, last channel, number entry, favorites.</summary>
public sealed class ChannelLineup
{
    public static readonly TimeSpan DigitTimeout = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private List<Channel> _channels = new();
    private readonly HashSet<string> _favorites = new();
    private int _current = -1, _previous = -1;
    private string _digits = "";
    private DateTimeOffset _lastDigit;

    public event Action<Channel, int>? Tuned;

    public IReadOnlyList<Channel> Channels { get { lock (_gate) return _channels.ToList(); } }
    public Channel? Current { get { lock (_gate) return At(_current); } }
    public int CurrentNumber { get { lock (_gate) return _current + 1; } }
    public string DigitBuffer { get { lock (_gate) return _digits; } }

    private Channel? At(int i) => i >= 0 && i < _channels.Count ? _channels[i] : null;

    public void Load(IEnumerable<Channel> channels)
    {
        lock (_gate)
        {
            var keepId = At(_current)?.Id;
            var prevId = At(_previous)?.Id;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _channels = channels.Where(c => seen.Add(c.Url)).ToList();
            _current = keepId is null ? -1 : _channels.FindIndex(c => c.Id == keepId);
            _previous = prevId is null ? -1 : _channels.FindIndex(c => c.Id == prevId);
        }
    }

    private Channel? Go(int index)
    {
        Channel? ch; int num;
        lock (_gate)
        {
            if (index < 0 || index >= _channels.Count) return null;
            if (index != _current) { _previous = _current; _current = index; }
            ch = _channels[_current]; num = _current + 1;
        }
        Tuned?.Invoke(ch, num);
        return ch;
    }

    public Channel? TuneNumber(int number) => Go(number - 1);

    public Channel? Next()
    {
        int n; lock (_gate) { if (_channels.Count == 0) return null; n = (_current + 1) % _channels.Count; }
        return Go(n);
    }

    public Channel? Previous()
    {
        int n; lock (_gate) { if (_channels.Count == 0) return null; n = _current <= 0 ? _channels.Count - 1 : _current - 1; }
        return Go(n);
    }

    public Channel? Last()
    {
        int n; lock (_gate) n = _previous;
        return Go(n);
    }

    public Channel? PressDigit(int digit, DateTimeOffset now, int maxDigits = 3)
    {
        if (digit < 0 || digit > 9) return null;
        bool commit;
        lock (_gate)
        {
            if (_digits.Length > 0 && now - _lastDigit >= DigitTimeout) _digits = "";
            _digits += digit.ToString();
            _lastDigit = now;
            commit = _digits.Length >= maxDigits;
        }
        return commit ? Commit() : null;
    }

    public Channel? Tick(DateTimeOffset now)
    {
        lock (_gate) { if (_digits.Length == 0 || now - _lastDigit < DigitTimeout) return null; }
        return Commit();
    }

    public Channel? Commit()
    {
        string d; lock (_gate) { d = _digits; _digits = ""; }
        return int.TryParse(d, out var n) ? TuneNumber(n) : null;
    }

    public bool ToggleFavorite(string id)
    {
        lock (_gate)
        {
            if (_channels.All(c => c.Id != id)) return false;
            return _favorites.Add(id) || !_favorites.Remove(id);
        }
    }

    public IReadOnlyList<Channel> Favorites { get { lock (_gate) return _channels.Where(c => _favorites.Contains(c.Id)).ToList(); } }
    public IReadOnlyList<string> Groups { get { lock (_gate) return _channels.Select(c => c.Group).Distinct().OrderBy(g => g).ToList(); } }
    public IReadOnlyList<Channel> InGroup(string group) { lock (_gate) return _channels.Where(c => c.Group == group).ToList(); }

    public LineupState Export()
    {
        lock (_gate) return new LineupState(At(_current)?.Id, At(_previous)?.Id, _favorites.ToList());
    }

    public void Restore(LineupState s)
    {
        lock (_gate)
        {
            _favorites.Clear();
            foreach (var f in s.Favorites) _favorites.Add(f);
            _current = s.CurrentId is null ? -1 : _channels.FindIndex(c => c.Id == s.CurrentId);
            _previous = s.PreviousId is null ? -1 : _channels.FindIndex(c => c.Id == s.PreviousId);
        }
    }
}
