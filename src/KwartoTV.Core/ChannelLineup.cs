namespace KwartoTV.Core;

public sealed record LineupState(string? CurrentId, string? PreviousId, List<string> Favorites, List<string>? Recent = null, Dictionary<string, int>? Numbers = null);

/// <summary>Cable-style lineup: numbered channels, up/down surfing, last channel, number entry, favorites.</summary>
public sealed class ChannelLineup
{
    public static readonly TimeSpan DigitTimeout = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private List<Channel> _channels = new();
    // Like cable: each channel keeps its number for good (by id). A channel that is hidden for a while leaves a gap,
    // nobody else's number moves, and it gets its own number back when it returns.
    private readonly Dictionary<string, int> _assigned = new();
    private List<int> _numbers = new();
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
    public int CurrentNumber { get { lock (_gate) return _current >= 0 && _current < _numbers.Count ? _numbers[_current] : 0; } }

    /// <summary>The channels with their numbers, in number order.</summary>
    public IReadOnlyList<(int Number, Channel Channel)> Numbered { get { lock (_gate) return _numbers.Zip(_channels).ToList(); } }

    /// <summary>The highest channel number given so far (sets how many digits a typed number has).</summary>
    public int MaxNumber { get { lock (_gate) return _assigned.Count == 0 ? 0 : _assigned.Values.Max(); } }

    /// <summary>The numbers given to channels (saved with the settings); set before the first Load.</summary>
    public void SetNumbers(IReadOnlyDictionary<string, int>? numbers)
    {
        lock (_gate)
        {
            _assigned.Clear();
            if (numbers is null) return;
            var used = new HashSet<int>();
            foreach (var (id, n) in numbers) if (n > 0 && used.Add(n)) _assigned[id] = n;
            Number();
        }
    }

    /// <summary>Numbers 1, 2, 3... again in the present order, closing the gaps (Settings → Renumber channels).</summary>
    public void Renumber()
    {
        lock (_gate)
        {
            _assigned.Clear();
            for (int i = 0; i < _channels.Count; i++) _assigned[_channels[i].Id] = i + 1;
            Number();
        }
        Changed?.Invoke();
    }

    // Gives new channels the next free number, then puts the list in number order (keeping current and previous).
    private void Number()
    {
        var keep = At(_current)?.Id; var prev = At(_previous)?.Id;
        int next = _assigned.Count == 0 ? 1 : _assigned.Values.Max() + 1;
        foreach (var c in _channels) if (!_assigned.ContainsKey(c.Id)) _assigned[c.Id] = next++;
        _channels = _channels.OrderBy(c => _assigned[c.Id]).ToList();
        _numbers = _channels.Select(c => _assigned[c.Id]).ToList();
        _current = keep is null ? -1 : _channels.FindIndex(c => c.Id == keep);
        _previous = prev is null ? -1 : _channels.FindIndex(c => c.Id == prev);
    }
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
            Number();
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
            ch = _channels[_current]; num = _numbers[_current];
            _recent.Remove(ch.Id); _recent.Insert(0, ch.Id);
            if (_recent.Count > MaxRecent) _recent.RemoveRange(MaxRecent, _recent.Count - MaxRecent);
        }
        Tuned?.Invoke(ch, num);
        return ch;
    }

    /// <summary>Tunes the channel with this number (null when no channel has it, also a gap left by a hidden channel).</summary>
    public Channel? TuneNumber(int number)
    {
        int i; lock (_gate) i = _numbers.IndexOf(number);
        return i < 0 ? null : Go(i);
    }

    /// <summary>Tunes a channel by its id (from a list drawn earlier).</summary>
    public Channel? TuneId(string id)
    {
        int i; lock (_gate) i = _channels.FindIndex(c => c.Id == id);
        return i < 0 ? null : Go(i);
    }

    /// <summary>The channel's number, or 0 when it is not in the lineup.</summary>
    public int NumberOf(string id) { lock (_gate) { var i = _channels.FindIndex(c => c.Id == id); return i < 0 ? 0 : _numbers[i]; } }

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

    /// <summary>A digit typed for a channel number. It goes when it has as many digits as the highest number (or [maxDigits]).</summary>
    public Channel? PressDigit(int digit, DateTimeOffset now, int maxDigits = 0)
    {
        if (digit < 0 || digit > 9) return null;
        bool commit;
        lock (_gate)
        {
            if (_digits.Length > 0 && now - _lastDigit >= DigitTimeout) _digits = "";
            _digits += digit.ToString();
            _lastDigit = now;
            var most = maxDigits > 0 ? maxDigits : Math.Max(3, (_assigned.Count == 0 ? 0 : _assigned.Values.Max()).ToString().Length);
            commit = _digits.Length >= most;
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
        lock (_gate) return new LineupState(At(_current)?.Id, At(_previous)?.Id, _favorites.ToList(), _recent.ToList(), new Dictionary<string, int>(_assigned));
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
