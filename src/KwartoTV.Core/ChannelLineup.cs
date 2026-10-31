namespace KwartoTV.Core;

public sealed record LineupState(string? CurrentId, string? PreviousId, List<string> Favorites, List<string>? Recent = null);

/// <summary>Cable-style lineup: numbered channels, up/down surfing, last channel, number entry, favorites.</summary>
public sealed class ChannelLineup
{
    public static readonly TimeSpan DigitTimeout = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private List<Channel> _channels = new();
    private readonly HashSet<string> _favorites = new();
    private int _current = -1, _previous = -1;
    // Channels watched lately, newest first (ids, so they survive changes to the lineup).
    private readonly List<string> _recent = new();
    public const int MaxRecent = 15;
    private string _digits = "";
    private DateTimeOffset _lastDigit;

    public event Action<Channel, int>? Tuned;
    /// <summary>Raised after a favorite is added or removed, so the shell can save.</summary>
    public event Action? FavoritesChanged;
    /// <summary>Raised after Load replaced the channels, so phones showing the lineup can refresh it.</summary>
    public event Action? Changed;

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
        Changed?.Invoke();
    }

    private Channel? Go(int index)
    {
        Channel? ch; int num;
        lock (_gate)
        {
            if (index < 0 || index >= _channels.Count) return null;
            if (index != _current) { _previous = _current; _current = index; }
            ch = _channels[_current]; num = _current + 1;
            _recent.Remove(ch.Id); _recent.Insert(0, ch.Id);
            if (_recent.Count > MaxRecent) _recent.RemoveRange(MaxRecent, _recent.Count - MaxRecent);
        }
        Tuned?.Invoke(ch, num);
        return ch;
    }

    public Channel? TuneNumber(int number) => Go(number - 1);

    /// <summary>
    /// Tunes a channel by its id. Use this from lists drawn earlier: numbers move when channels are added or hidden,
    /// the id stays the same.
    /// </summary>
    public Channel? TuneId(string id)
    {
        int i; lock (_gate) i = _channels.FindIndex(c => c.Id == id);
        return i < 0 ? null : Go(i);
    }

    /// <summary>The channel's current number (1-based), or 0 when it is not in the lineup.</summary>
    public int NumberOf(string id) { lock (_gate) return _channels.FindIndex(c => c.Id == id) + 1; }

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

    /// <summary>Returns true when the channel is now a favorite, false when it was removed (or does not exist).</summary>
    public bool ToggleFavorite(string id)
    {
        bool nowFavorite;
        lock (_gate)
        {
            if (_channels.All(c => c.Id != id)) return false;
            nowFavorite = _favorites.Add(id);
            if (!nowFavorite) _favorites.Remove(id);
        }
        FavoritesChanged?.Invoke();
        return nowFavorite;
    }

    public bool IsFavorite(string id) { lock (_gate) return _favorites.Contains(id); }

    /// <summary>Marks or unmarks a favorite by id, also for a channel that is not in the lineup (yet). True when it changed.</summary>
    public bool SetFavorite(string id, bool on)
    {
        bool changed;
        lock (_gate) changed = on ? _favorites.Add(id) : _favorites.Remove(id);
        if (changed) FavoritesChanged?.Invoke();
        return changed;
    }

    public IReadOnlyList<Channel> Favorites { get { lock (_gate) return _channels.Where(c => _favorites.Contains(c.Id)).ToList(); } }
    public IReadOnlyList<string> Groups { get { lock (_gate) return _channels.Select(c => c.Group).Distinct().OrderBy(ChannelCurator.CategoryRank).ThenBy(g => g).ToList(); } }
    public IReadOnlyList<Channel> InGroup(string group) { lock (_gate) return _channels.Where(c => c.Group == group).ToList(); }

    public LineupState Export()
    {
        lock (_gate) return new LineupState(At(_current)?.Id, At(_previous)?.Id, _favorites.ToList(), _recent.ToList());
    }

    /// <summary>Channels watched lately that are in the lineup now, newest first.</summary>
    public IReadOnlyList<Channel> Recent
    {
        get
        {
            lock (_gate)
            {
                var byId = _channels.ToDictionary(c => c.Id, c => c);
                return _recent.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
            }
        }
    }

    public void Restore(LineupState s)
    {
        lock (_gate)
        {
            _recent.Clear();
            if (s.Recent is not null) _recent.AddRange(s.Recent.Distinct().Take(MaxRecent));
            _favorites.Clear();
            foreach (var f in s.Favorites) _favorites.Add(f);
            _current = s.CurrentId is null ? -1 : _channels.FindIndex(c => c.Id == s.CurrentId);
            _previous = s.PreviousId is null ? -1 : _channels.FindIndex(c => c.Id == s.PreviousId);
        }
    }
}
