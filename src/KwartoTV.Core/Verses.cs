namespace KwartoTV.Core;

/// <summary>Bible verses for the lock screen (King James Version, public domain), changing on a schedule.</summary>
public static class Verses
{
    public sealed record Verse(string Text, string Reference);

    /// <summary>How often the verse can change, in minutes (0 = no verse). The lock screen setting cycles through these.</summary>
    public static readonly int[] Choices = { 60, 180, 1440, 30, 15, 0 };

    public static string Describe(int minutes) => minutes switch
    {
        <= 0 => "off",
        1440 => "every day",
        60 => "every hour",
        < 60 => $"every {minutes} minutes",
        _ when minutes % 60 == 0 => $"every {minutes / 60} hours",
        _ => $"every {minutes} minutes",
    };

    /// <summary>The next choice after [current] (for a cycling button).</summary>
    public static int Next(int current)
    {
        var i = Array.IndexOf(Choices, current);
        return Choices[(i + 1) % Choices.Length];
    }

    /// <summary>
    /// The verse for [now], changing every [everyMinutes] (counted from local midnight, so "every hour" changes on the
    /// hour and "every day" at midnight). Null when verses are off.
    /// </summary>
    public static Verse? For(DateTime now, int everyMinutes)
    {
        if (everyMinutes <= 0) return null;
        var slot = (long)Math.Floor((now - new DateTime(2024, 1, 1)).TotalMinutes / everyMinutes);
        // Steps through the list in a scattered order, so neighbouring slots do not show neighbouring verses.
        var index = (int)(((slot * 37) % All.Count + All.Count) % All.Count);
        return All[index];
    }

    public static readonly IReadOnlyList<Verse> All = new Verse[]
    {
        new("For God so loved the world, that he gave his only begotten Son, that whosoever believeth in him should not perish, but have everlasting life.", "John 3:16"),
        new("The LORD is my shepherd; I shall not want.", "Psalm 23:1"),
        new("I can do all things through Christ which strengtheneth me.", "Philippians 4:13"),
        new("Trust in the LORD with all thine heart; and lean not unto thine own understanding. In all thy ways acknowledge him, and he shall direct thy paths.", "Proverbs 3:5–6"),
        new("For I know the thoughts that I think toward you, saith the LORD, thoughts of peace, and not of evil, to give you an expected end.", "Jeremiah 29:11"),
        new("Fear thou not; for I am with thee: be not dismayed; for I am thy God: I will strengthen thee; yea, I will help thee; yea, I will uphold thee with the right hand of my righteousness.", "Isaiah 41:10"),
        new("Come unto me, all ye that labour and are heavy laden, and I will give you rest.", "Matthew 11:28"),
        new("And we know that all things work together for good to them that love God, to them who are the called according to his purpose.", "Romans 8:28"),
        new("Have not I commanded thee? Be strong and of a good courage; be not afraid, neither be thou dismayed: for the LORD thy God is with thee whithersoever thou goest.", "Joshua 1:9"),
        new("God is our refuge and strength, a very present help in trouble.", "Psalm 46:1"),
        new("Be still, and know that I am God: I will be exalted among the heathen, I will be exalted in the earth.", "Psalm 46:10"),
        new("Thy word is a lamp unto my feet, and a light unto my path.", "Psalm 119:105"),
        new("But they that wait upon the LORD shall renew their strength; they shall mount up with wings as eagles; they shall run, and not be weary; and they shall walk, and not faint.", "Isaiah 40:31"),
        new("But seek ye first the kingdom of God, and his righteousness; and all these things shall be added unto you.", "Matthew 6:33"),
        new("And now abideth faith, hope, charity, these three; but the greatest of these is charity.", "1 Corinthians 13:13"),
        new("It is of the LORD's mercies that we are not consumed, because his compassions fail not. They are new every morning: great is thy faithfulness.", "Lamentations 3:22–23"),
        new("This is the day which the LORD hath made; we will rejoice and be glad in it.", "Psalm 118:24"),
        new("Be careful for nothing; but in every thing by prayer and supplication with thanksgiving let your requests be made known unto God.", "Philippians 4:6"),
        new("And the peace of God, which passeth all understanding, shall keep your hearts and minds through Christ Jesus.", "Philippians 4:7"),
        new("For God hath not given us the spirit of fear; but of power, and of love, and of a sound mind.", "2 Timothy 1:7"),
        new("The LORD is my light and my salvation; whom shall I fear? the LORD is the strength of my life; of whom shall I be afraid?", "Psalm 27:1"),
        new("O taste and see that the LORD is good: blessed is the man that trusteth in him.", "Psalm 34:8"),
        new("Delight thyself also in the LORD; and he shall give thee the desires of thine heart.", "Psalm 37:4"),
        new("Cast thy burden upon the LORD, and he shall sustain thee: he shall never suffer the righteous to be moved.", "Psalm 55:22"),
        new("He that dwelleth in the secret place of the most High shall abide under the shadow of the Almighty.", "Psalm 91:1"),
        new("I will lift up mine eyes unto the hills, from whence cometh my help. My help cometh from the LORD, which made heaven and earth.", "Psalm 121:1–2"),
        new("Commit thy works unto the LORD, and thy thoughts shall be established.", "Proverbs 16:3"),
        new("A merry heart doeth good like a medicine: but a broken spirit drieth the bones.", "Proverbs 17:22"),
        new("The name of the LORD is a strong tower: the righteous runneth into it, and is safe.", "Proverbs 18:10"),
        new("Thou wilt keep him in perfect peace, whose mind is stayed on thee: because he trusteth in thee.", "Isaiah 26:3"),
        new("Ye are the light of the world. A city that is set on an hill cannot be hid.", "Matthew 5:14"),
        new("Let your light so shine before men, that they may see your good works, and glorify your Father which is in heaven.", "Matthew 5:16"),
        new("Peace I leave with you, my peace I give unto you: not as the world giveth, give I unto you. Let not your heart be troubled, neither let it be afraid.", "John 14:27"),
        new("Jesus saith unto him, I am the way, the truth, and the life: no man cometh unto the Father, but by me.", "John 14:6"),
        new("These things I have spoken unto you, that in me ye might have peace. In the world ye shall have tribulation: but be of good cheer; I have overcome the world.", "John 16:33"),
        new("Now the God of hope fill you with all joy and peace in believing, that ye may abound in hope, through the power of the Holy Ghost.", "Romans 15:13"),
        new("And let us not be weary in well doing: for in due season we shall reap, if we faint not.", "Galatians 6:9"),
        new("Now faith is the substance of things hoped for, the evidence of things not seen.", "Hebrews 11:1"),
        new("Jesus Christ the same yesterday, and to day, and for ever.", "Hebrews 13:8"),
        new("If any of you lack wisdom, let him ask of God, that giveth to all men liberally, and upbraideth not; and it shall be given him.", "James 1:5"),
        new("Casting all your care upon him; for he careth for you.", "1 Peter 5:7"),
        new("We love him, because he first loved us.", "1 John 4:19"),
        new("O give thanks unto the LORD; for he is good: for his mercy endureth for ever.", "Psalm 136:1"),
        new("The LORD is good, a strong hold in the day of trouble; and he knoweth them that trust in him.", "Nahum 1:7"),
        new("The LORD thy God in the midst of thee is mighty; he will save, he will rejoice over thee with joy; he will rest in his love, he will joy over thee with singing.", "Zephaniah 3:17"),
        new("Be strong and of a good courage, fear not, nor be afraid of them: for the LORD thy God, he it is that doth go with thee; he will not fail thee, nor forsake thee.", "Deuteronomy 31:6"),
        new("He hath shewed thee, O man, what is good; and what doth the LORD require of thee, but to do justly, and to love mercy, and to walk humbly with thy God?", "Micah 6:8"),
        new("Rejoice evermore. Pray without ceasing. In every thing give thanks: for this is the will of God in Christ Jesus concerning you.", "1 Thessalonians 5:16–18"),
        new("Let the words of my mouth, and the meditation of my heart, be acceptable in thy sight, O LORD, my strength, and my redeemer.", "Psalm 19:14"),
        new("But Jesus beheld them, and said unto them, With men this is impossible; but with God all things are possible.", "Matthew 19:26"),
        new("But God commendeth his love toward us, in that, while we were yet sinners, Christ died for us.", "Romans 5:8"),
        new("For his anger endureth but a moment; in his favour is life: weeping may endure for a night, but joy cometh in the morning.", "Psalm 30:5"),
    };
}

/// <summary>The photos of the lock screen slideshow.</summary>
public static class Slideshow
{
    private static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };
    public const int MaxPhotos = 5000;

    /// <summary>The photos in [folder] and its subfolders (at most MaxPhotos), shuffled with [seed]. Empty when the folder is missing.</summary>
    public static IReadOnlyList<string> Photos(string? folder, int seed)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return Array.Empty<string>();
        var list = new List<string>();
        try
        {
            foreach (var f in Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System }))
            {
                if (Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())) list.Add(f);
                if (list.Count >= MaxPhotos) break;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        var rnd = new Random(seed);
        for (int i = list.Count - 1; i > 0; i--) { var j = rnd.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
        return list;
    }
}
