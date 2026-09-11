using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Runtime.CompilerServices;
using MB.ComTools.Apps.Content.Services.Agent;

namespace MB.ComTools.Apps.Content.Services;

public partial class AgentService
{
    // ===============================================================
    // Helpers + internal model
    // ===============================================================

    private static CourseInfo? PickBestMatch(IReadOnlyList<CourseInfo> courses, string query)
    {
        if (courses.Count == 0)
        {
            return null;
        }

        var exact = courses.FirstOrDefault(c => SameText(c.Title, query));
        if (exact is not null)
        {
            return exact;
        }

        var contains = courses.FirstOrDefault(c => ContainsText(c.Title, query) || ContainsText(query, c.Title));
        if (contains is not null)
        {
            return contains;
        }

        var queryTerms = ExtractSearchTerms(query);
        if (queryTerms.Count == 0)
        {
            return null;
        }

        var minMatches = queryTerms.Count == 1 ? 1 : 2;
        var scored = courses
            .Select(course => new
            {
                Course = course,
                Matches = CountMatchedTerms(course, queryTerms)
            })
            .Where(item => item.Matches >= minMatches)
            .OrderByDescending(item => item.Matches)
            .ThenBy(item => item.Course.Title.Length)
            .FirstOrDefault();

        return scored?.Course;
    }

    private static int CountMatchedTerms(CourseInfo course, IReadOnlyList<string> queryTerms)
    {
        var searchable = NormalizeSearchText(string.Join(" ", course.Title, course.Summary, course.Category));
        return queryTerms.Count(term => searchable.Contains(term, StringComparison.Ordinal));
    }

    /// <summary>
    /// Distinguishes a BaseEntryId from a human title. Platform ids are long,
    /// single tokens; a failed direct lookup still falls back to title search.
    /// </summary>
    private static bool LooksLikeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains(' ') || !IdentifierLikeRegex.IsMatch(value))
        {
            return false;
        }

        return true;
    }

    private static string? ExtractFromDeepLink(string? deepLink, Regex pattern)
    {
        if (string.IsNullOrWhiteSpace(deepLink))
        {
            return null;
        }

        var match = pattern.Match(deepLink);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static List<T> Distinct<T>(IEnumerable<T> items, Func<T, string> keySelector)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<T>();

        foreach (var item in items)
        {
            if (seen.Add(keySelector(item)))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static bool HasAnyProperty(JsonElement element, params string[] names)
    {
        return element.ValueKind == JsonValueKind.Object && TryGetProperty(element, out _, names);
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement property, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out property))
                {
                    return true;
                }
            }
        }

        property = default;
        return false;
    }

    private static bool TryGetString(JsonElement element, out string? value, params string[] names)
    {
        value = null;

        if (!TryGetProperty(element, out var property, names) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = property.GetString();

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        value = text.Trim();
        return true;
    }

    private static bool TryGetStringOrNumber(JsonElement element, out string? value, params string[] names)
    {
        value = null;

        if (!TryGetProperty(element, out var property, names))
        {
            return false;
        }

        value = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString()?.Trim(),
            JsonValueKind.Number => property.ToString(),
            _ => null
        };

        return !string.IsNullOrWhiteSpace(value);
    }

    private static int? TryGetInt(JsonElement element, params string[] names)
    {
        if (TryGetProperty(element, out var property, names)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var value))
        {
            return value;
        }

        return null;
    }

    private static bool? TryGetBool(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var property, names))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static bool SameText(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && left.Trim().Equals(right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool ContainsText(string? haystack, string? needle) =>
        !string.IsNullOrWhiteSpace(haystack)
        && !string.IsNullOrWhiteSpace(needle)
        && haystack.Contains(needle.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var normalized = value.Trim();

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength].TrimEnd() + "...";
    }

    private static string? TruncateWords(string? value, int maxWords)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var words = Regex.Split(value.Trim(), @"\s+");

        return words.Length <= maxWords
            ? string.Join(" ", words)
            : string.Join(" ", words.Take(maxWords)) + "...";
    }

    private static string OneLine(string value) =>
        Regex.Replace(value, @"\s*[\r\n]+\s*", " ").Trim();

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "de";
        }

        var normalized = language.Trim().ToLowerInvariant();

        return normalized.Length >= 2 ? normalized[..2] : "de";
    }

    private static string Localize(string? language, string german, string english) =>
        IsGerman(language) ? german : english;

    private static string[] Localize(string? language, string[] german, string[] english) =>
        IsGerman(language) ? german : english;

    private static bool IsGerman(string? language) =>
        string.IsNullOrWhiteSpace(language) || language.StartsWith("de", StringComparison.OrdinalIgnoreCase);

    private static string DescribeToolCalls(IEnumerable<ToolCallRequest> calls) =>
        string.Join(", ", calls.Select(c => $"{c.Tool}({c.Query})"));

    // ===============================================================
    // Internal model
    // ===============================================================

    private enum AgentIntent
    {
        CourseSearch,
        CourseDetails,
        CourseCompare,
        SkillSearch,
        SkillDetails,
        SkillCourses,
        ProfileSearch,
        ProfileDetails,
        ProfileCourses,
        LearningPath,
        LearningRecommendation,
        DivisionOverview,
        Collections,
        Bookmarks,
        Progress,
        Capabilities,
        SmallTalk,
        OutOfScope,
        Clarify
    }

    private enum ReferenceType
    {
        None,
        Active,
        First,
        Second,
        Third,
        Fourth,
        Fifth
    }

    private enum EntityKind
    {
        None,
        Course,
        Profile,
        Skill,
        Collection,
        Division
    }

    private enum ArgumentNeed
    {
        None,
        Optional,
        Topic,
        Identifier
    }

    private sealed record ToolDescriptor(
        string Name,
        EntityKind EntityKind,
        ArgumentNeed ArgumentNeed,
        string Description);

    private readonly record struct UserPerception(
        string OriginalMessage,
        string ConversationId,
        DateTimeOffset Timestamp);

    private sealed record ToolCallRequest(
        string Tool,
        string Query,
        ReferenceType Reference)
    {
        /// <summary>
        /// Structured arguments the backend resolves itself, such as a divisionId.
        /// The language model only ever supplies <see cref="Query"/>.
        /// </summary>
        public Dictionary<string, string> Args { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ReasoningResult(
        AgentIntent Intent,
        string Language,
        string? ClarificationQuestion,
        Dictionary<string, string> Slots,
        IReadOnlyList<ToolCallRequest> ToolCalls);

    private sealed record ExecutionPlan(
        AgentIntent Intent,
        string Language,
        string? ClarificationQuestion,
        Dictionary<string, string> Slots,
        IReadOnlyList<ToolCallRequest> FirstRound,
        bool NeedsData)
    {
        public string UserMessage { get; init; } = string.Empty;
    }

    private sealed record ToolOutcome(string Tool, bool Failed = false)
    {
        public List<CourseInfo> Courses { get; init; } = [];
        public List<ProfileInfo> Profiles { get; init; } = [];
        public List<SkillInfo> Skills { get; init; } = [];
        public List<CollectionInfo> Collections { get; init; } = [];
        public List<DivisionInfo> Divisions { get; init; } = [];
        public ProgressInfo? Progress { get; init; }
        public bool RequiresSignIn { get; init; }
    }

    private sealed class Evidence
    {
        public List<CourseInfo> Courses { get; } = [];
        public List<ProfileInfo> Profiles { get; } = [];
        public List<SkillInfo> Skills { get; } = [];
        public List<CollectionInfo> Collections { get; } = [];
        public List<DivisionInfo> Divisions { get; } = [];
        public List<string> ExecutedTools { get; } = [];
        public List<string> FailedTools { get; } = [];
        public ProgressInfo? Progress { get; private set; }
        public bool RequiresSignIn { get; private set; }

        public bool HasAny =>
            Courses.Count > 0 ||
            Profiles.Count > 0 ||
            Skills.Count > 0 ||
            Collections.Count > 0 ||
            Divisions.Count > 0 ||
            Progress is not null;

        public void Merge(ToolOutcome outcome)
        {
            if (outcome.Failed)
            {
                FailedTools.Add(outcome.Tool);
            }
            else
            {
                ExecutedTools.Add(outcome.Tool);
            }

            RequiresSignIn |= outcome.RequiresSignIn;
            Progress ??= outcome.Progress;

            // A detail call replaces the search snippet of the same item.
            foreach (var course in outcome.Courses)
            {
                var existing = Courses.FindIndex(c =>
                    SameText(c.Id, course.Id) || SameText(c.Title, course.Title));

                if (existing >= 0)
                {
                    if (course.HasDetail)
                    {
                        Courses[existing] = course;
                    }

                    continue;
                }

                Courses.Add(course);
            }

            foreach (var profile in outcome.Profiles)
            {
                var existing = Profiles.FindIndex(p =>
                    SameText(p.Id, profile.Id) || SameText(p.Title, profile.Title));

                if (existing >= 0)
                {
                    if (profile.Courses.Count > Profiles[existing].Courses.Count)
                    {
                        Profiles[existing] = profile;
                    }

                    continue;
                }

                Profiles.Add(profile);
            }

            // get_skill carries the description that taxonomy nodes lack, while
            // search_skills carries the children that get_skill lacks — so the two
            // records for one skill are combined instead of the later one being lost.
            foreach (var skill in outcome.Skills)
            {
                var existing = Skills.FindIndex(s =>
                    SameText(s.Id, skill.Id) || SameText(s.Name, skill.Name));

                if (existing >= 0)
                {
                    Skills[existing] = Combine(Skills[existing], skill);
                    continue;
                }

                Skills.Add(skill);
            }

            MergeInto(Collections, outcome.Collections, c => c.Id ?? c.Title);
            MergeInto(Divisions, outcome.Divisions, d => d.Id ?? d.Name);
        }

        private static SkillInfo Combine(SkillInfo existing, SkillInfo incoming) =>
            existing with
            {
                Id = Richer(existing.Id, incoming.Id),
                Description = Richer(existing.Description, incoming.Description),
                Parent = Richer(existing.Parent, incoming.Parent),
                Children = existing.Children.Count >= incoming.Children.Count
                    ? existing.Children
                    : incoming.Children,
                RequiredCount = Math.Max(existing.RequiredCount, incoming.RequiredCount),
                OptionalCount = Math.Max(existing.OptionalCount, incoming.OptionalCount),
                CourseCount = Math.Max(existing.CourseCount, incoming.CourseCount)
            };

        private static string? Richer(string? existing, string? incoming) =>
            string.IsNullOrWhiteSpace(existing) ? incoming : existing;

        private static void MergeInto<T>(List<T> target, IEnumerable<T> source, Func<T, string> keySelector)
        {
            var keys = target.Select(keySelector).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var item in source)
            {
                if (keys.Add(keySelector(item)))
                {
                    target.Add(item);
                }
            }
        }
    }

    private sealed record AnswerResult(string Message, int CardCount, string Prose = "")
    {
        public string? PendingSlot { get; init; }

        /// <summary>
        /// The cards this answer actually contains — the only items a positional
        /// follow-up may refer to.
        /// </summary>
        public RenderedCards Rendered { get; init; } = new();

        public List<ChatCardDto> StructuredCards { get; init; } = [];

        public List<string> StructuredSuggestions { get; init; } = [];
    }

    private sealed class RenderedCards
    {
        public List<EntityRef> Courses { get; } = [];
        public List<EntityRef> Profiles { get; } = [];
        public List<EntityRef> Collections { get; } = [];
        public List<EntityRef> Divisions { get; } = [];

        /// <summary>
        /// Cards in the exact order they were appended to the answer. Positional
        /// follow-ups ("the second one") resolve against this list only.
        /// </summary>
        public List<EntityRef> Addressable { get; } = [];

        public List<ChatCardDto> StructuredCards { get; } = [];

        public int Count => Addressable.Count;

        /// <summary>
        /// Dominant card type of the last answer (for logging / active fallbacks).
        /// Positional resolution uses <see cref="Addressable"/>, not this value.
        /// </summary>
        public EntityKind PrimaryKind
        {
            get
            {
                if (Addressable.Count == 0)
                {
                    return EntityKind.None;
                }

                // The first card kind dominates mixed answers (e.g. bookmarks:
                // profiles then courses) so "the first one" stays consistent.
                return Addressable[0].Kind;
            }
        }
    }

    private sealed record CourseInfo(
        string? Id,
        string Title,
        string? Summary,
        string? Duration,
        string? Platform,
        string? Category,
        string? Instructor,
        string? Objectives,
        string? DeepLink,
        string? Requirement,
        string? DifficultyLevel,
        bool IsActive,
        bool HasDetail);

    private sealed record ProfileInfo(
        string? Id,
        string Title,
        string? Summary,
        string? Division,
        string? Department,
        int RequiredCount,
        int OptionalCount,
        string? DeepLink,
        List<CourseInfo> Courses);

    private sealed record SkillInfo(
        string? Id,
        string Name,
        string? Description,
        string? Parent,
        List<string> Children,
        int CourseCount)
    {
        // Only set for the skill groups of a profile, which is what makes those
        // cards profile-specific instead of a copy of the global taxonomy.
        public int RequiredCount { get; init; }
        public int OptionalCount { get; init; }
    }

    private sealed record CollectionInfo(
        string? Id,
        string Title,
        string? Summary,
        int ItemCount,
        string? DeepLink);

    private sealed record ProgressInfo(
        List<CourseInfo> CompletedCourses,
        List<CourseInfo> InProgressCourses,
        string? SiteCompletionRate);

    private sealed record DivisionInfo(
        string? Id,
        string Name,
        List<string> Departments,
        string? DeepLink);

    private sealed record ConversationTurn(string Role, string Text);

    private sealed class ConversationState
    {
        public object Gate { get; } = new();
        public SemaphoreSlim ProcessingGate { get; } = new(1, 1);
        public List<ConversationTurn> History { get; } = [];
        public List<EntityRef> LastCourses { get; set; } = [];
        public List<EntityRef> LastProfiles { get; set; } = [];
        public List<EntityRef> LastSkills { get; set; } = [];
        public List<EntityRef> LastCollections { get; set; } = [];
        public List<EntityRef> LastDivisions { get; set; } = [];

        /// <summary>
        /// Visual card order of the previous answer — the only list a bare
        /// positional follow-up may address.
        /// </summary>
        public List<EntityRef> LastAddressableItems { get; set; } = [];

        public EntityRef? ActiveCourse { get; set; }
        public EntityRef? ActiveProfile { get; set; }
        public EntityRef? ActiveSkill { get; set; }
        public Dictionary<string, string> Slots { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? PendingSlot { get; set; }
        public string Language { get; set; } = "de";
        public AgentIntent LastIntent { get; set; } = AgentIntent.CourseSearch;

        /// <summary>
        /// Dominant card type of the previous answer (first card in visual order).
        /// </summary>
        public EntityKind LastRenderedKind { get; set; } = EntityKind.None;

        /// <summary>
        /// Keyword from the last topical search — used for "ähnliche/weitere Kurse"
        /// when ActiveCourse is unset (multi-card answers).
        /// </summary>
        public string? LastSearchTopic { get; set; }

        public DateTimeOffset LastUpdatedAt { get; set; }

        public void Reset()
        {
            History.Clear();
            LastCourses = [];
            LastProfiles = [];
            LastSkills = [];
            LastCollections = [];
            LastDivisions = [];
            LastAddressableItems = [];
            ActiveCourse = null;
            ActiveProfile = null;
            ActiveSkill = null;
            LastRenderedKind = EntityKind.None;
            LastSearchTopic = null;
            Slots.Clear();
            PendingSlot = null;
        }
    }

    private sealed record EntityRef(string? Id, string DisplayName, EntityKind Kind);
}
