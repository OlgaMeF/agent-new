using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Runtime.CompilerServices;
using MB.ComTools.Apps.Content.Services.Agent;
using MB.ComTools.Apps.Setup.Mcp.Dtos;

namespace MB.ComTools.Apps.Content.Services;

public partial class AgentService
{
    // ===============================================================
    // PHASE 4 — EXECUTION
    // ===============================================================

    private async Task<Evidence> ExecutePlanAsync(
        ExecutionPlan plan,
        ConversationState conversation,
        CancellationToken cancellationToken)
    {


        var evidence = new Evidence();

        if (!plan.NeedsData || plan.FirstRound.Count == 0)
        {
            return evidence;
        }

        ApplyDifficultyArgs(plan, plan.FirstRound);
        await RunRoundAsync(plan.FirstRound, evidence, cancellationToken);
        _logger.LogInformation(
            "RESPONSE_EVIDENCE courses={Count}",
            evidence.Courses.Count);

        foreach (var c in evidence.Courses)
        {
            _logger.LogInformation(
                "RESPONSE_COURSE {Title}",
                c.Title);
        }

        _logger.LogInformation(
            "COURSE_DEBUG afterRunRound evidenceCourses={Count}",
            evidence.Courses.Count);

        _logger.LogInformation(
            "SKILL_DEBUG skills={Skills}",
            string.Join(", ", evidence.Skills.Select(x => x.Name)));

        // Drop unrelated MCP hits before round 2 so similar-course can retry
        // with PreferCourseKeyword when the first query only returned noise.
        FilterCourseSearchEvidence(plan, evidence, conversation);

        var secondRound = DeriveSecondRound(plan, evidence);

        if (secondRound.Count > 0)
        {
            _logger.LogInformation(
                "PHASE4_ROUND2 toolCalls={ToolCalls}",
                DescribeToolCalls(secondRound));

            ApplyDifficultyArgs(plan, secondRound);
            await RunRoundAsync(secondRound, evidence, cancellationToken);

        }

        FilterCourseSearchEvidence(plan, evidence, conversation);
        FilterProfileCourseEvidence(plan, evidence);
        FocusSkills(plan, evidence);

        return evidence;
    }            
    private static void FilterCourseSearchEvidence(
        ExecutionPlan plan,
        Evidence evidence,
        ConversationState conversation)
    {
        if (plan.Intent != AgentIntent.CourseSearch || evidence.Courses.Count == 0)
        {
            return;
        }

        var filterMessage = IsCourseFollowUpRequest(plan.UserMessage)
            ? plan.FirstRound.FirstOrDefault(call => call.Tool.Equals("search_courses", StringComparison.OrdinalIgnoreCase))?.Query
            : plan.UserMessage;
        var terms = ExtractSearchTerms(filterMessage ?? string.Empty);

        if (terms.Count == 0)
        {
            return;
        }

        var requiredTerms = terms
            .Where(term => term.Contains('#') || term.Contains('.') || term.Any(char.IsDigit))
            .ToList();
        var activeCourse = conversation.ActiveCourse;

        // "Similar courses" must not list the course the user is already looking
        // at, so this removal stays unconditional for similar-course follow-ups.
        var isSimilar = plan.Slots.ContainsKey("similarCourse")
            || IsCourseFollowUpRequest(plan.UserMessage);

        if (activeCourse is not null && isSimilar)
        {
            evidence.Courses.RemoveAll(course =>
                SameText(course.Id, activeCourse.Id)
                || SameText(course.Title, activeCourse.DisplayName));
        }

        var matching = evidence.Courses
            .Where(course =>
            {
                var searchable = NormalizeSearchText(string.Join(
                    " ",
                    course.Title,
                    course.Summary,
                    course.Category));

                if (requiredTerms.Count > 0
                    && requiredTerms.Any(term => !searchable.Contains(term, StringComparison.Ordinal)))
                {
                    return false;
                }

                return terms.Any(term => searchable.Contains(term, StringComparison.Ordinal));
            })
            .ToList();

        // The term filter only narrows a result set. Letting it remove everything
        // turned successful MCP searches into "I found nothing", because the terms
        // are matched against the card text, not against what the server indexed.
        if (matching.Count == 0)
        {
            // Similar-course searches must not keep unrelated MCP noise (e.g. HTML
            // courses for query "kommunikation"). Empty triggers a broader round 2.
            if (isSimilar)
            {
                evidence.Courses.Clear();
            }

            return;
        }

        evidence.Courses.Clear();
        evidence.Courses.AddRange(matching);
    }

    private static List<string> ExtractSearchTerms(string message)
    {
        var stopWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "finde", "finden", "für", "fuer", "mich", "mir", "kurse", "kurs", "kursen", "profile", "profil", "profiles", "profilem", "profilen", "bereich", "area", "areas", "zum", "zur",
            "zu", "über", "ueber", "im", "in", "der", "die", "das", "den", "dem", "des",
            "ein", "eine", "einen", "einer", "eines", "mit", "von", "auf", "und", "oder", "bitte",
            "gibt", "es", "the", "course", "courses", "please", "show", "me", "on",
            "find", "for", "to", "what", "which", "are", "is", "ist", "was", "about", "learn", "learning",
            "suche", "zeig", "zeige", "zeigen", "such", "nach", "etwas", "irgendwelche",
            "welche", "welcher", "welches", "welchen", "wem", "wen", "wie", "wo", "wann",
            "warum", "wieso", "weshalb", "kannst", "koennen", "können", "hast", "haben",
            "gebe", "gib", "gibt", "lass", "lasse", "mal", "doch", "noch", "auch",
            "details", "detail", "info", "infos", "information", "informationen",
            "besser", "beste", "besten", "eignet", "eignen", "sich", "am", "schon", "gut", "aus",
            "kann", "ich", "du", "wir", "kenntnisse", "kentnisse", "erweitern", "erzähl", "erzaehl",
            "mehr", "tell", "more", "about", "beginner", "beginners", "anfaenger", "anfänger",
            "einsteiger", "intermediate", "expert", "experte", "experts", "niveau", "schwierigkeit",
            "schwierigkeitsniveau", "level", "fortgeschritten", "advanced", "empfehlung", "empfehlen", "externe", "interne"
        };

        return Regex.Matches(message.ToLowerInvariant(), @"[\p{L}\p{N}][\p{L}\p{N}+#.-]*")
            .Select(match => match.Value)
            .Where(term => !stopWords.Contains(term)
                           && (term.Length >= 3 || term.Contains('#') || IsShortSearchKeyword(term)))
            .Select(NormalizeSearchTerm)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsShortSearchKeyword(string term) =>
        term is "ki" or "ai" or "ui" or "ux" or "it" or "hr" or "po" or "sm" or "fc";

    private static string NormalizeSearchTerm(string term) =>
        StemSearchWord(term
            .Replace("managment", "management", StringComparison.Ordinal)
            .Replace("manangement", "management", StringComparison.Ordinal));

    private static string NormalizeSearchText(string? value) =>
        Regex.Replace(value?.ToLowerInvariant() ?? string.Empty, @"[^\p{L}\p{N}+#.-]+", " ");

    private static void FilterProfileCourseEvidence(
        ExecutionPlan plan,
        Evidence evidence)
    {
        if (plan.Intent != AgentIntent.ProfileCourses)
        {
            return;
        }

        var message = plan.UserMessage;
        var wantsRequired = message.Contains("pflicht", StringComparison.OrdinalIgnoreCase)
            || message.Contains("mandatory", StringComparison.OrdinalIgnoreCase)
            || message.Contains("required", StringComparison.OrdinalIgnoreCase);

        var wantsOptional = message.Contains("optional", StringComparison.OrdinalIgnoreCase)
            || message.Contains("wahl", StringComparison.OrdinalIgnoreCase)
            || message.Contains("elective", StringComparison.OrdinalIgnoreCase);

        if (wantsRequired && !wantsOptional)
        {
            evidence.Courses.RemoveAll(course => !IsRequired(course.Requirement));
            return;
        }

        if (wantsOptional && !wantsRequired)
        {
            evidence.Courses.RemoveAll(course => IsRequired(course.Requirement));
        }
    }

    /// <summary>
    /// search_skills always answers with the complete taxonomy. When the user asked
    /// about one skill, the answer has to be about that skill and its sub-skills
    /// instead of every root category.
    /// </summary>
    private static void FocusSkills(ExecutionPlan plan, Evidence evidence)
    {
        if (plan.Intent is not (AgentIntent.SkillSearch or AgentIntent.SkillDetails)
            || evidence.Skills.Count < 2)
        {
            return;
        }

        var named = FindNamedSkill(plan, evidence);

        if (named is null)
        {
            // SkillDetails with a topic must not dump the entire taxonomy.
            if (plan.Intent == AgentIntent.SkillDetails
                && plan.Slots.TryGetValue("topic", out var topic)
                && !string.IsNullOrWhiteSpace(topic))
            {
                evidence.Skills.Clear();
            }

            return;
        }

        var children = evidence.Skills
            .Where(s => !ReferenceEquals(s, named)
                        && (named.Children.Contains(s.Name, StringComparer.OrdinalIgnoreCase)
                            || SameText(s.Parent, named.Name)))
            .ToList();

        evidence.Skills.Clear();
        evidence.Skills.Add(named);
        evidence.Skills.AddRange(children);
    }

    /// <summary>
    /// Matches against the skill names the tools actually returned, so this stays
    /// language-independent. The longest name wins, otherwise "Data Analytics"
    /// would be shadowed by "Data".
    /// </summary>
    private static SkillInfo? FindNamedSkill(
        ExecutionPlan plan,
        Evidence evidence)
    {
        var topic = plan.Slots.TryGetValue("topic", out var value)
            ? value?.Trim()
            : null;

        if (!string.IsNullOrWhiteSpace(topic))
        {
            var exactTopicMatch = evidence.Skills
                .FirstOrDefault(skill => SameText(skill.Name, topic));

            if (exactTopicMatch is not null)
            {
                return exactTopicMatch;
            }
        }

        var fromMessage = evidence.Skills
            .Where(skill =>
                skill.Name.Length >= 3 &&
                Regex.IsMatch(
                    plan.UserMessage,
                    $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(skill.Name)}(?![\p{{L}}\p{{N}}])",
                    RegexOptions.IgnoreCase))
            .OrderByDescending(skill => skill.Name.Length)
            .FirstOrDefault();

        if (fromMessage is not null)
        {
            return fromMessage;
        }

        var tokens = ExpandSkillSearchTokens(topic ?? plan.UserMessage);
        if (tokens.Count == 0)
        {
            return null;
        }

        return evidence.Skills
            .Select(skill => (
                Skill: skill,
                Score: tokens.Count(token =>
                    ContainsText(skill.Name, token) || ContainsText(skill.Description, token))))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Skill.Name.Length)
            .Select(x => x.Skill)
            .FirstOrDefault();
    }

    private static List<string> ExpandSkillSearchTokens(string? query)
    {
        var tokens = ExtractSearchTerms(query ?? string.Empty);
        if (string.IsNullOrWhiteSpace(query))
        {
            return tokens;
        }

        var text = query.ToLowerInvariant();
        var extra = new List<string>();

        if (text.Contains("data analytics")
            || text.Contains("datenanalyse")
            || text.Contains("data analysis"))
        {
            extra.AddRange(["datenkompetenz", "analytics", "data", "business intelligence"]);
        }

        if (text.Contains("genai") || text.Contains("gen ai") || Regex.IsMatch(text, @"\bki\b"))
        {
            extra.AddRange(["genai", "ki-basics", "ki"]);
        }

        return tokens
            .Concat(extra)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task RunRoundAsync(
        IReadOnlyList<ToolCallRequest> calls,
        Evidence evidence,
        CancellationToken cancellationToken)
    {
        // All tool calls of a round go over HTTP to the MCP endpoint and are
        // independent, so they run concurrently.
        var outcomes = await Task.WhenAll(
            calls.Take(MaxToolCallsPerRound)
                 .Select(call => DispatchAsync(call, cancellationToken)));

        foreach (var outcome in outcomes)
        {
            if (outcome is null)
            {
                continue;
            }

            evidence.Merge(outcome);
        }
    }

    /// <summary>
    /// Second thinking round. It either deepens a search result (details for a
    /// comparison, courses of a profile) or falls back to a different tool when
    /// the first round found nothing.
    /// </summary>
private static List<ToolCallRequest> DeriveSecondRound(
    ExecutionPlan plan,
    Evidence evidence)
{
    var calls = new List<ToolCallRequest>();
    var alreadyCalled = plan.FirstRound
        .Select(c => c.Tool)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (plan.Slots.TryGetValue(
            "similarCourse",
            out var courseName)
        && !string.IsNullOrWhiteSpace(courseName))
    {
        if (evidence.Courses.Count == 0)
        {
            var tried = plan.FirstRound
                .Where(c => c.Tool.Equals("search_courses", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Query)
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in new[]
                     {
                         PreferCourseKeyword(courseName),
                         SanitizeTopicQuery(ExtractCourseSearchTopic(courseName)),
                         SanitizeTopicQuery(StripTitleDecorators(courseName))
                     })
            {
                if (string.IsNullOrWhiteSpace(candidate) || tried.Contains(candidate))
                {
                    continue;
                }

                calls.Add(new ToolCallRequest("search_courses", candidate, ReferenceType.None));
                break;
            }
        }

        return calls;
    }
        switch (plan.Intent)
        {
            case AgentIntent.CourseCompare:
            {
                // A comparison needs full detail per candidate, not search snippets.
                foreach (var candidate in evidence.Courses.Where(c => !c.HasDetail).Take(3))
                {
                    var identifier = candidate.Id ?? candidate.Title;

                    if (!string.IsNullOrWhiteSpace(identifier))
                    {
                        calls.Add(new ToolCallRequest("get_course", identifier, ReferenceType.None));
                    }
                }

                break;
            }

            case AgentIntent.CourseDetails:
            {
                if (!alreadyCalled.Contains("get_course") && evidence.Courses.Count > 0)
                {
                    var course = evidence.Courses[0];

                    calls.Add(new ToolCallRequest(
                        "get_course",
                        course.Id ?? course.Title,
                        ReferenceType.None));
                }

                break;
            }

            case AgentIntent.ProfileDetails:
            {
                if (!alreadyCalled.Contains("get_profile_skills") && evidence.Profiles.Count == 1)
                {
                    calls.Add(new ToolCallRequest(
                        "get_profile_skills",
                        evidence.Profiles[0].Id ?? evidence.Profiles[0].Title,
                        ReferenceType.None));
                }

                break;
            }

            case AgentIntent.ProfileCourses:
            case AgentIntent.LearningPath:
            {
                // search_profiles returns no course assignments; get_profile does.
                if (!alreadyCalled.Contains("get_profile"))
                {
                    foreach (var profile in evidence.Profiles.Where(p => p.Courses.Count == 0).Take(2))
                    {
                        var identifier = profile.Id ?? profile.Title;

                        if (!string.IsNullOrWhiteSpace(identifier))
                        {
                            calls.Add(new ToolCallRequest("get_profile", identifier, ReferenceType.None));
                        }
                    }
                }

                break;
            }

            case AgentIntent.ProfileSearch:
            {
                var divisionName = plan.Slots.TryGetValue("division", out var division)
                    ? division
                    : null;

                if (evidence.Profiles.Count == 0
                    && evidence.Divisions.Count > 0
                    && !string.IsNullOrWhiteSpace(divisionName))
                {
                    var matchingDivision = evidence.Divisions.FirstOrDefault(d =>
                        SameText(d.Name, divisionName)
                        || SameText(d.Id, divisionName)
                        || ContainsText(d.Name, divisionName)
                        || ContainsText(divisionName, d.Name));

                    if (matchingDivision?.Id is not null)
                    {
                        calls.Add(new ToolCallRequest("search_profiles", string.Empty, ReferenceType.None)
                        {
                            Args = { ["divisionId"] = matchingDivision.Id }
                        });
                    }
                }

                break;
            }

            case AgentIntent.SkillSearch:
            case AgentIntent.SkillDetails:
            {
                if (evidence.Skills.Count == 0
                    && !alreadyCalled.Contains("search_skills"))
                {
                    calls.Add(new ToolCallRequest(
                        "search_skills",
                        string.Empty,
                        ReferenceType.None));
                    break;
                }

                // Taxonomy nodes have no description, so explaining a skill needs
                // get_skill on top of the tree.
                if (!alreadyCalled.Contains("get_skill"))
                {
                    var named = FindNamedSkill(plan, evidence);

                    if (named is not null && string.IsNullOrWhiteSpace(named.Description))
                    {
                        calls.Add(new ToolCallRequest("get_skill", named.Name, ReferenceType.None));
                    }
                }

                break;
            }

            case AgentIntent.SkillCourses:
            {
                // get_courses_by_tag resolves the tag name by exact match, so a
                // phrase like "GenAI courses" yields nothing and full text wins.
                var topic = plan.FirstRound.FirstOrDefault()?.Query;

                if (evidence.Courses.Count == 0
                    && !string.IsNullOrWhiteSpace(topic)
                    && !alreadyCalled.Contains("search_courses"))
                {
                    calls.Add(new ToolCallRequest("search_courses", topic, ReferenceType.None));
                }

                break;
            }

            case AgentIntent.CourseSearch:
            {
                // A course topic is not necessarily a skill/tag. Retry the course
                // search with normalized alternatives instead of sending the phrase
                // to get_courses_by_tag, which only accepts an exact tag name.
                var topic = plan.FirstRound.FirstOrDefault()?.Query;

                if (evidence.Courses.Count == 0)
                {
                    calls.AddRange(BuildCourseSearchAlternatives(topic)
                        .Select(query => new ToolCallRequest("search_courses", query, ReferenceType.None)));
                }

                break;
            }

            case AgentIntent.DivisionOverview:
            {
                if (evidence.Profiles.Count > 0)
                {
                    break;
                }

                // "Which profiles exist in division X" — search_profiles matches
                // profile names, not divisions, so resolve the division id first.
                // Prefer an explicit slot (positional follow-up) over scanning the
                // raw user message, which often only says "the second area".
                var divisionName = plan.Slots.TryGetValue("division", out var slottedDivision)
                    && !string.IsNullOrWhiteSpace(slottedDivision)
                        ? slottedDivision
                        : null;

                var division = evidence.Divisions.FirstOrDefault(d =>
                    !string.IsNullOrWhiteSpace(d.Id)
                    && (SameText(d.Name, divisionName)
                        || SameText(d.Id, divisionName)
                        || ContainsText(d.Name, divisionName)
                        || ContainsText(plan.UserMessage, d.Name)));

                if (division is not null)
                {
                    calls.Add(new ToolCallRequest("search_profiles", string.Empty, ReferenceType.None)
                    {
                        Args = { ["divisionId"] = division.Id! }
                    });
                }
                else if (evidence.Divisions.Count == 0 && !alreadyCalled.Contains("search_profiles"))
                {
                    // Profiles carry their division and department names, so an area
                    // overview stays possible even when the division tool fails.
                    calls.Add(new ToolCallRequest("search_profiles", string.Empty, ReferenceType.None));
                }

                break;
            }
        }

        return calls;
    }

    private static IReadOnlyList<string> BuildCourseSearchAlternatives(string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return [];
        }

        var normalized = topic.Trim().ToLowerInvariant()
            .Replace("managment", "management", StringComparison.Ordinal)
            .Replace("manage­ment", "management", StringComparison.Ordinal);

        var alternatives = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            normalized
        };

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length > 1)
        {
            var stemmed = words.Select(StemSearchWord).ToArray();
            alternatives.Add(string.Join(' ', stemmed));

            for (var index = 0; index < words.Length; index++)
            {
                var reduced = words
                    .Select((word, wordIndex) => wordIndex == index ? StemSearchWord(word) : word)
                    .ToArray();
                alternatives.Add(string.Join(' ', reduced));
            }
        }

        return alternatives
            .Where(query => !string.Equals(query, normalized, StringComparison.OrdinalIgnoreCase))
            .Take(MaxToolCallsPerRound)
            .ToList();
    }

    private static string StemSearchWord(string word) => word switch
    {
        "agiles" or "agile" => "agil",
        "strategische" or "strategischer" => "strategisch",
        "technische" or "technischer" => "technisch",
        _ => word
    };

    /// <summary>
    /// Tag names are short labels such as "GenAI" or "Data Analytics", never a
    /// full question.
    /// </summary>
    private static bool LooksLikeTagName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var words = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return words.Length <= 3 && value.Length <= 40 && !value.Contains('?');
    }

    private async Task<ToolOutcome?> DispatchAsync(
        ToolCallRequest call,
        CancellationToken cancellationToken)
    {
        try
        {
            return call.Tool.ToLowerInvariant() switch
            {
                "search_courses" => await SearchCoursesAsync(call, cancellationToken),
                "get_course" => await GetCourseAsync(call.Query, cancellationToken),
                "get_courses_by_tag" => await GetCoursesByTagAsync(call.Query, cancellationToken),

                "search_skills" => await GetSkillHierarchyAsync(cancellationToken),
                "get_skill" => await GetSkillAsync(call.Query, cancellationToken),

                "search_profiles" => await SearchProfilesAsync(call, cancellationToken),
                "get_profile" => await GetProfileAsync(call.Query, cancellationToken),
                "get_profile_skills" => await GetProfileSkillsAsync(call.Query, cancellationToken),

                "get_divisions" => await GetDivisionsAsync(cancellationToken),
                "search_collections" => await SearchCollectionsAsync(call.Query, cancellationToken),
                "get_my_bookmarks" => await GetBookmarksAsync(cancellationToken),
                "get_my_progress" => await GetProgressAsync(cancellationToken),

                _ => null
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {Tool} failed for query {Query}.", call.Tool, call.Query);

            return new ToolOutcome(call.Tool, Failed: true);
        }
    }

    // ---------------- course tools ----------------

    private async Task<ToolOutcome> SearchCoursesAsync(
        ToolCallRequest call,
        CancellationToken cancellationToken)
    {
        call.Args.TryGetValue("difficultyLevel", out var difficultyLevel);

        var raw = await _mcpClientService.SearchCoursesAsync(
            query: call.Query,
            limit: DefaultSearchLimit,
            offset: 0,
            difficultyLevel: difficultyLevel,
            cancellationToken: cancellationToken);

        var courses = ExtractCourses(raw);

        if (!TryParseMcpPayload(raw, out _))
        {
            return new ToolOutcome("search_courses", Failed: true);
        }

        return new ToolOutcome("search_courses")
        {
            Courses = courses
        };
    }

    private async Task<ToolOutcome> GetCoursesByTagAsync(string query, CancellationToken cancellationToken)
    {
        var raw = await _mcpClientService.GetCoursesByTagAsync(query, cancellationToken: cancellationToken);

        if (!TryParseMcpPayload(raw, out _))
        {
            return new ToolOutcome("get_courses_by_tag", Failed: true);
        }

        return new ToolOutcome("get_courses_by_tag")
        {
            Courses = ExtractCourses(raw)
        };
    }

    /// <summary>
    /// Accepts an id or a title. A title is resolved through a search first, which
    /// is what makes "tell me more about the Empathy course" work.
    /// </summary>
    private async Task<ToolOutcome> GetCourseAsync(string query, CancellationToken cancellationToken)
    {
        if (LooksLikeIdentifier(query))
        {
            var direct = ExtractCourses(
                await _mcpClientService.GetCourseAsync(query, cancellationToken));

            if (direct.Count > 0)
            {
                return new ToolOutcome("get_course")
                {
                    Courses = direct.Select(c => c with { HasDetail = true }).ToList()
                };
            }
        }

        var searchRaw = await _mcpClientService.SearchCoursesAsync(
            query: query,
            limit: DefaultSearchLimit,
            offset: 0,
            cancellationToken: cancellationToken);

        

        var candidates = ExtractCourses(searchRaw);
        var best = PickBestMatch(candidates, query);

        if (best is null)
        {
            return new ToolOutcome("get_course");
        }

        if (string.IsNullOrWhiteSpace(best.Id))
        {
            // No id to deepen with, but the search snippet already answers a lot.
            return new ToolOutcome("get_course") { Courses = [best] };
        }

        var detail = ExtractCourses(
            await _mcpClientService.GetCourseAsync(best.Id, cancellationToken));

        return new ToolOutcome("get_course")
        {
            Courses = detail.Count > 0
                ? detail.Select(c => c with { HasDetail = true }).ToList()
                : [best]
        };
    }

    // ---------------- skill tools ----------------

    private async Task<ToolOutcome> GetSkillHierarchyAsync(CancellationToken cancellationToken)
    {
        var raw = await _mcpClientService.GetSkillHierarchyAsync(cancellationToken);

        return new ToolOutcome("search_skills")
        {
            Skills = ExtractSkills(raw)
        };
    }

    private async Task<ToolOutcome> GetSkillAsync(string query, CancellationToken cancellationToken)
    {
        var raw = await _mcpClientService.GetSkillAsync(query, cancellationToken);
        var skills = ExtractSkills(raw);

        if (skills.Count == 0)
        {
            // get_skill misses on synonyms; the taxonomy tree lets us match locally.
            var hierarchyRaw = await _mcpClientService.GetSkillHierarchyAsync(cancellationToken);
            var all = ExtractSkills(hierarchyRaw);

            skills = all
                    .Where(s => ContainsText(s.Name, query) || ContainsText(s.Description, query))
                .Take(6)
                .ToList();

            if (skills.Count == 0)
            {
                var tokens = ExtractSearchTerms(query);
                skills = all
                    .Select(s => (
                        Skill: s,
                        Score: tokens.Count(t =>
                            ContainsText(s.Name, t) || ContainsText(s.Description, t))))
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .ThenByDescending(x => x.Skill.Name.Length)
                    .Select(x => x.Skill)
                    .Take(6)
                    .ToList();
            }
        }

        return new ToolOutcome("get_skill")
        {
            Skills = skills
        };
    }

    // ---------------- profile tools ----------------

    private async Task<ToolOutcome> SearchProfilesAsync(
        ToolCallRequest call,
        CancellationToken cancellationToken)
    {
        // divisionId is never produced by the language model; phase 4 resolves a
        // division name to its id first and passes it here (UC12).
        call.Args.TryGetValue("divisionId", out var divisionId);

        var raw = await _mcpClientService.SearchProfilesAsync(
            query: call.Query,
            limit: DefaultSearchLimit,
            offset: 0,
            divisionId: divisionId,
            cancellationToken: cancellationToken);

        if (!TryParseMcpPayload(raw, out _))
        {
            return new ToolOutcome("search_profiles", Failed: true);
        }

        return new ToolOutcome("search_profiles")
        {
            Profiles = ExtractProfiles(raw)
        };
    }

    private async Task<ToolOutcome> GetProfileAsync(string query, CancellationToken cancellationToken)
    {
        if (LooksLikeIdentifier(query))
        {
            var direct = ExtractProfiles(
                await _mcpClientService.GetProfileAsync(query, cancellationToken));

            if (direct.Count > 0)
            {
                return BuildProfileOutcome(direct);
            }
        }

        var searchRaw = await _mcpClientService.SearchProfilesAsync(
            query: query,
            limit: DefaultSearchLimit,
            offset: 0,
            cancellationToken: cancellationToken);

        var candidates = ExtractProfiles(searchRaw);

        if (candidates.Count == 0)
        {
            return new ToolOutcome("get_profile");
        }

        var match = SelectBestProfileMatch(candidates, query);

        // Several equally plausible matches are an answer in themselves (UC6):
        // show the list and let the user pick instead of guessing.
        if (match is null && candidates.Count > 1)
        {
            return new ToolOutcome("search_profiles") { Profiles = candidates };
        }

        match ??= candidates[0];

        if (string.IsNullOrWhiteSpace(match.Id))
        {
            return BuildProfileOutcome([match]);
        }

        var profiles = ExtractProfiles(
            await _mcpClientService.GetProfileAsync(match.Id, cancellationToken));

        return BuildProfileOutcome(profiles.Count > 0 ? profiles : [match]);
    }

    private static ProfileInfo? SelectBestProfileMatch(
        IReadOnlyList<ProfileInfo> candidates,
        string query)
    {
        var normalizedQuery = NormalizeMatchText(query);
        var exact = candidates.FirstOrDefault(p =>
            NormalizeMatchText(p.Title) == normalizedQuery);

        if (exact is not null)
        {
            return exact;
        }

        var matches = candidates
            .Where(p => NormalizeMatchText(p.Title).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                        || normalizedQuery.Contains(NormalizeMatchText(p.Title), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => NormalizeMatchText(p.Title) == normalizedQuery)
            .ThenByDescending(p => p.Title.Length)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    private static string NormalizeMatchText(string? value) =>
        Regex.Replace(value?.Trim() ?? string.Empty, @"[^\p{L}\p{N}]+", " ")
            .Trim()
            .ToLowerInvariant();

    private static ToolOutcome BuildProfileOutcome(List<ProfileInfo> profiles)
    {
        return new ToolOutcome("get_profile")
        {
            Profiles = profiles,
            Courses = profiles.SelectMany(p => p.Courses).ToList()
        };
    }

    private async Task<ToolOutcome> GetProfileSkillsAsync(string query, CancellationToken cancellationToken)
    {
        var normalized = NormalizeProfileQuery(query);
        var raw = await _mcpClientService.GetProfileSkillsAsync(normalized, cancellationToken);
        var profiles = ExtractProfiles(raw);
        var skills = ExtractSkills(raw);
        var courses = ExtractCourses(raw);

        // Exact role strings with articles ("ein Product Owner") miss; resolve via search.
        if (profiles.Count == 0 && skills.Count == 0 && !string.IsNullOrWhiteSpace(normalized))
        {
            var searchRaw = await _mcpClientService.SearchProfilesAsync(
                query: normalized,
                limit: DefaultSearchLimit,
                offset: 0,
                cancellationToken: cancellationToken);

            var candidates = ExtractProfiles(searchRaw);
            var match = SelectBestProfileMatch(candidates, normalized) ?? candidates.FirstOrDefault();
            if (match is not null && !string.IsNullOrWhiteSpace(match.Id ?? match.Title))
            {
                raw = await _mcpClientService.GetProfileSkillsAsync(
                    match.Id ?? match.Title!,
                    cancellationToken);
                profiles = ExtractProfiles(raw);
                skills = ExtractSkills(raw);
                courses = ExtractCourses(raw);

                if (profiles.Count == 0 && match is not null)
                {
                    profiles = [match];
                }
            }
        }

        return new ToolOutcome("get_profile_skills")
        {
            Profiles = profiles,
            Skills = skills,
            Courses = courses
        };
    }

    private static string NormalizeProfileQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var normalized = query.Trim();
        normalized = Regex.Replace(
            normalized,
            @"^(?:ein(?:e|em|er|es)?\s+|den\s+|die\s+|das\s+|der\s+|the\s+|a\s+|an\s+)",
            string.Empty,
            RegexOptions.IgnoreCase);
        return normalized.Trim();
    }

    // ---------------- structure, collections, user ----------------

    private async Task<ToolOutcome> GetDivisionsAsync(CancellationToken cancellationToken)
    {
        var raw = await _mcpClientService.GetDivisionsAsync(cancellationToken);

        return new ToolOutcome("get_divisions")
        {
            Divisions = ExtractDivisions(raw)
        };
    }

    private async Task<ToolOutcome> SearchCollectionsAsync(string query, CancellationToken cancellationToken)
    {
        var raw = await _mcpClientService.SearchCollectionsAsync(cancellationToken);
        var collections = ExtractCollections(raw);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var terms = ExtractSearchTerms(query);
            var matches = collections
                .Where(c =>
                {
                    var searchable = NormalizeSearchText(string.Join(" ", c.Title, c.Summary));
                    return terms.Count > 0
                        && terms.Any(term => searchable.Contains(term, StringComparison.Ordinal));
                })
                .ToList();

            // Positional follow-ups pass the exact card title; term stemming can
            // miss it, so fall back to a direct title match before giving up.
            if (matches.Count == 0)
            {
                matches = collections
                    .Where(c => SameText(c.Title, query) || ContainsText(c.Title, query))
                    .ToList();
            }

            if (matches.Count > 0)
            {
                collections = matches;
            }
        }

        return new ToolOutcome("search_collections")
        {
            Collections = collections
        };
    }

    private async Task<ToolOutcome> GetBookmarksAsync(CancellationToken cancellationToken)
    {
        var raw = await _mcpClientService.GetMyBookmarksAsync(cancellationToken);

        // A missing token is reported inside a successful response, not as an
        // HTTP error, so the payload has to be inspected for it.
        if (string.IsNullOrWhiteSpace(raw) || ContainsAuthError(raw))
        {
            return new ToolOutcome("get_my_bookmarks", Failed: true) { RequiresSignIn = true };
        }

        return new ToolOutcome("get_my_bookmarks")
        {
            Courses = ExtractCourses(raw),
            Profiles = ExtractProfiles(raw)
        };
    }

    private async Task<ToolOutcome> GetProgressAsync(CancellationToken cancellationToken)
    {
        var raw = await _mcpClientService.GetMyProgressAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(raw) || ContainsAuthError(raw))
        {
            return new ToolOutcome("get_my_progress", Failed: true) { RequiresSignIn = true };
        }

        var progress = ExtractProgress(raw);

        return progress is null
            ? new ToolOutcome("get_my_progress", Failed: true)
            : new ToolOutcome("get_my_progress")
            {
                Progress = progress,
                Courses = progress.CompletedCourses
                    .Concat(progress.InProgressCourses)
                    .ToList()
            };
    }

    /// <summary>
    /// get_my_progress already returns the two buckets. Re-deriving them from a
    /// progressPercentage dropped every completed course, because completed
    /// entries carry completedAt instead of a percentage — and a fractional
    /// percentage such as 33.5 failed the integer parse and vanished from both
    /// lists.
    /// </summary>
    private static ProgressInfo? ExtractProgress(string rawMcpResponse)
    {
        if (!TryParseMcpPayload(rawMcpResponse, out var root))
        {
            return null;
        }

        var completed = ExtractCoursesFromArray(root, "completedCourses", "CompletedCourses");
        var inProgress = ExtractCoursesFromArray(root, "inProgressCourses", "InProgressCourses");

        var rates = CollectObjects(root, element =>
            HasAnyProperty(element, "completionRate", "CompletionRate"),
            maxResults: 5);
        var rate = rates.FirstOrDefault() is { } rateObject
            && TryGetStringOrNumber(rateObject, out var rateValue, "completionRate", "CompletionRate")
                ? rateValue
                : null;

        return new ProgressInfo(completed, inProgress, rate);
    }

    private static List<CourseInfo> ExtractCoursesFromArray(JsonElement root, params string[] propertyNames)
    {
        return TryGetProperty(root, out var array, propertyNames)
            && array.ValueKind == JsonValueKind.Array
                ? ExtractCourses(array)
                : new List<CourseInfo>();
    }

    /// <summary>
    /// Copies difficulty from plan slots / user text onto search_courses Args so MCP
    /// can filter Beginner / Intermediate / Expert without changing the topic query.
    /// </summary>
    private static void ApplyDifficultyArgs(
        ExecutionPlan plan,
        IEnumerable<ToolCallRequest> calls)
    {
        var difficulty = ResolveDifficultyLevelArg(plan.Slots, plan.UserMessage);

        if (difficulty is null)
        {
            return;
        }

        foreach (var call in calls)
        {
            if (!call.Tool.Equals("search_courses", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!call.Args.ContainsKey("difficultyLevel"))
            {
                call.Args["difficultyLevel"] = difficulty;
            }
        }
    }

    private static string? ResolveDifficultyLevelArg(
        IReadOnlyDictionary<string, string> slots,
        string? userMessage)
    {
        if (slots.TryGetValue("difficultyLevel", out var fromSlot))
        {
            var normalized = McpDifficultyLevel.NormalizeFilter(fromSlot);

            if (normalized is not null)
            {
                return normalized;
            }
        }

        if (slots.TryGetValue("audience", out var audience))
        {
            var fromAudience = McpDifficultyLevel.NormalizeFilter(audience);

            // Audience is Beginner/Intermediate/Expert only — never Unset.
            if (fromAudience is not null
                && !fromAudience.Equals(McpDifficultyLevel.Unset, StringComparison.OrdinalIgnoreCase))
            {
                return fromAudience;
            }
        }

        if (!string.IsNullOrWhiteSpace(userMessage)
            && TryInferDifficultyLevel(userMessage, out var inferred))
        {
            return inferred;
        }

        return null;
    }

    private static bool TryInferDifficultyLevel(string message, out string filterValue)
    {
        filterValue = string.Empty;

        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        // Prefer explicit niveau phrases over bare words like "advanced".
        if (!McpDifficultyLevel.TryParse(message, out var level) || level == 0)
        {
            return false;
        }

        // Avoid matching "advanced" inside unrelated compound words by requiring
        // a clear difficulty cue in German or English.
        var text = message.ToLowerInvariant()
            .Replace("ä", "ae", StringComparison.Ordinal)
            .Replace("ö", "oe", StringComparison.Ordinal)
            .Replace("ü", "ue", StringComparison.Ordinal);

        var hasCue = text.Contains("anfaenger", StringComparison.Ordinal)
            || text.Contains("einsteiger", StringComparison.Ordinal)
            || text.Contains("beginner", StringComparison.Ordinal)
            || text.Contains("fortgeschritten", StringComparison.Ordinal)
            || text.Contains("intermediate", StringComparison.Ordinal)
            || text.Contains("experte", StringComparison.Ordinal)
            || text.Contains("expert", StringComparison.Ordinal)
            || text.Contains("schwierigkeit", StringComparison.Ordinal)
            || text.Contains("niveau", StringComparison.Ordinal)
            || text.Contains(" fuer anfaenger", StringComparison.Ordinal)
            || text.Contains(" for beginners", StringComparison.Ordinal);

        if (!hasCue)
        {
            return false;
        }

        filterValue = McpDifficultyLevel.ToFilterValue(level)!;
        return true;
    }
}
