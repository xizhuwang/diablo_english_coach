using System.Net;
using System.Text.Json;

namespace DiabloEnglishCoach;

internal static class FastTranslationSelfTest
{
    internal static async Task AddChecksAsync(Dictionary<string, object> checks)
    {
        checks["advertising_rejected"] = !OcrService.LooksLikeEnglishSubtitle("WWW.GTOPUP.TOP 10K Platinum $4.99 fast delivery");
        checks["chat_prefix_rejected"] = !OcrService.LooksLikeEnglishSubtitle("[7] merchant: come here");
        checks["normal_equipment_not_rejected"] = OcrService.LooksLikeEnglishSubtitle("Your summons deal increased damage.");
        checks["fast_quest_compound"] = FastTranslationService.TryLocal("Head Forward and Search for Leoric", true) == "往前走，尋找 Leoric。";
        checks["fast_quest_leave"] = FastTranslationService.TryLocal("Leave Mad King's Breach", true) == "離開 Mad King's Breach。";
        checks["fast_quest_return"] = FastTranslationService.TryLocal("Return to the gate", true) == "返回 the gate。";
        checks["no_invented_compound_action"] = FastTranslationService.TryLocal("Head to the gate and defeat the guard", true) is null;
        checks["no_inverted_negative_action"] = FastTranslationService.TryLocal("Do not enter the gate", true) is null;
        checks["no_negative_compound_action"] = FastTranslationService.TryLocal("Do not head forward and search for Leoric", true) is null;
        checks["no_story_prose_as_instruction"] = FastTranslationService.TryLocal("I want to head to the gate", true) is null;
        checks["teaching_removes_character_name"] = CoachService.LearningPhrase("Head Forward and Search for Leoric") == "search for";
        var hallucination = CoachService.ParseModelReply("Search for Leoric", """{"simple_english":"Look for the boss","traditional_chinese":"尋找頭領","keywords":[]}""");
        checks["invented_boss_is_not_spoken"] = !hallucination.UsedLocalModel && !hallucination.TraditionalChinese.Contains("頭領");
        checks["dialogue_not_interpreted_as_quest"] = FastTranslationService.TryLocal("Head to the gate", false) is null;
        checks["local_translation_cleans_game_terms"] = FastTranslationService.CleanLocalModelReply(
            "The skill deals damage to the undead.", "翻譯：技術會對不死之人造成損害。") == "技能會對不死族造成傷害。";
        checks["busy_model_one_thread"] = AdaptiveLoadMonitor.InferenceBudget(new(30, 0, true), 8) == 1;
        checks["idle_model_four_threads"] = AdaptiveLoadMonitor.InferenceBudget(new(30, 5000, false), 8) == 4;
        checks["high_cpu_one_thread"] = AdaptiveLoadMonitor.InferenceBudget(new(80, 5000, true), 8) == 1;
        checks["small_cpu_thread_bound"] = AdaptiveLoadMonitor.InferenceBudget(new(20, 5000, false), 2) == 1;
        var reply = new CoachReply("x", "x", "翻譯", [new("summon", "召喚")], true, Advice: "先選一個核心技能");
        checks["narration_contains_english_and_build"] = CoachForm.Narration(reply).Contains("summon") && CoachForm.Narration(reply).Contains("核心技能");
        var narration = new NarrationGuard();
        narration.ObserveDialogue(1_000, true);
        checks["dialogue_blocks_new_coach_audio"] = !narration.MayStart(7_999, true, false, false, true) &&
            narration.MayStart(8_000, true, false, false, true);
        narration.ObserveSpaceKey(9_000, 0);
        checks["space_key_delays_coach_audio"] = !narration.MayStart(13_999, true, false, false, true);
        narration.ObserveQuestPanel(20_000, true);
        narration.ObserveQuestPanel(21_000, false);
        checks["hidden_quest_panel_delays_cutscene_audio"] = !narration.MayStart(28_999, true, false, false, true) &&
            narration.MayStart(29_000, true, false, false, true);
        checks["background_never_starts_audio"] = !narration.MayStart(30_000, true, false, false, false);
        var planner = new IdleLessonPlanner();
        planner.Reset(0);
        planner.ObserveLesson(IdleLessonPlanner.IntervalMs - 1, [], resetSchedule: false);
        checks["teaching_not_starved_by_model"] = planner.TryNext(IdleLessonPlanner.IntervalMs, true, new CoachConfig(), requireQuietWindow: false) is not null;
        checks["teaching_no_catchup_burst"] = planner.TryNext(IdleLessonPlanner.IntervalMs + 1, true, new CoachConfig(), requireQuietWindow: false) is null;

        var folder = Path.Combine(Path.GetTempPath(), "diablo-translation-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var handler = new FakeHandler();
            var path = Path.Combine(folder, "cache.json");
            var azure = new CoachConfig
            {
                TranslationProvider = TranslationProviders.Azure,
                OnlineTranslationEnabled = true,
                AzureTranslatorRegion = "eastasia"
            };
            using (var service = new FastTranslationService(new HttpClient(handler), path, () => "test-secret"))
            {
                var repaired = await service.TranslateAsync("Deckard Cain\nI need y0ur help.", false, azure, default);
                checks["ocr_repairs_before_fast_translation"] = repaired.Text == "我需要你的幫忙。" && handler.Calls == 0;
                var offline = await service.TranslateAsync("There is danger nearby.", false,
                    new CoachConfig { TranslationProvider = TranslationProviders.Disabled }, default);
                checks["offline_sends_nothing"] = handler.Calls == 0 && offline.Text is null;
                await service.TranslateAsync("Visit www.gtopup.top for cheap gold", false, azure, default);
                checks["ads_never_uploaded"] = handler.Calls == 0;
                var result = await service.TranslateAsync("There is danger nearby.", false, azure, default);
                var cached = await service.TranslateAsync("There is danger nearby.", false, azure, default);
                checks["online_translation_and_cache"] = result.Text == "附近有危險。" && cached.Text == result.Text && handler.Calls == 1;
                checks["azure_text_sample"] = result.Text ?? "null";
                checks["azure_official_endpoint"] = handler.LastUri is { } uri &&
                    uri.Host == "api.cognitive.microsofttranslator.com" && uri.AbsolutePath == "/translate";
                checks["secret_not_in_uri"] = handler.LastUri is not null && !handler.LastUri.ToString().Contains("test-secret");
                checks["azure_key_in_header"] = handler.LastKey == "test-secret" && handler.LastRegion == "eastasia";
                checks["azure_posts_json"] = handler.LastMethod == HttpMethod.Post && handler.LastBody.Contains("There is danger nearby.");
                checks["quota_counts_uncached_only"] = service.CharactersUsed == "There is danger nearby.".Length;
                var longResult = await service.TranslateAsync(new string('a', 490) + " danger is nearby", false, azure, default);
                checks["long_input_supported_by_azure"] = longResult.Text is not null && handler.Calls == 2;
            }
            var noKeyHandler = new FakeHandler();
            using (var noKey = new FastTranslationService(new HttpClient(noKeyHandler), Path.Combine(folder, "no-key.json"), () => null))
            {
                var result = await noKey.TranslateAsync("There is danger nearby.", false, azure, default);
                checks["missing_key_never_calls_api"] = result.Text is null && noKeyHandler.Calls == 0;
            }
            using (var reloaded = new FastTranslationService(new HttpClient(new FakeHandler()), path, () => null))
            {
                var result = await reloaded.TranslateAsync("There is danger nearby.", false, azure, default);
                checks["cache_survives_restart"] = result.Text == "附近有危險。";
            }
            var localHandler = new FakeLocalHandler();
            var local = new CoachConfig
            {
                TranslationProvider = TranslationProviders.LocalOllama,
                TranslationModel = "qwen3.5:0.8b",
                OllamaUrl = "http://127.0.0.1:11434",
                InferenceThreads = 1
            };
            using (var localService = new FastTranslationService(new HttpClient(localHandler),
                Path.Combine(folder, "local.json"), () => null))
            {
                var partials = new List<string>();
                var result = await localService.TranslateAsync("The skill deals damage.", false, local, default, partials.Add);
                checks["streaming_shows_chinese_before_completion"] = partials.Count > 0 && partials[0] == "這個技能" && result.FirstTextMs is not null;
                checks["streaming_text_samples"] = partials;
                checks["streaming_requested"] = JsonDocument.Parse(localHandler.LastBody).RootElement.GetProperty("stream").GetBoolean();
                var cached = await localService.TranslateAsync("The skill deals damage.", false, local, default);
                checks["local_model_translation_and_cache"] = result.Text == "這個技能造成傷害。" &&
                    cached.Text == result.Text && localHandler.Calls == 1;
                checks["local_model_uses_loopback_only"] = localHandler.LastUri?.Host == "127.0.0.1" &&
                    localHandler.LastUri.Port == 11434 && localHandler.LastUri.AbsolutePath == "/api/chat";
                checks["local_model_prompt_has_game_glossary"] = localHandler.LastBody.Contains("summons") &&
                    localHandler.LastBody.Contains("qwen3.5:0.8b");
                await localService.WarmAsync(local, default);
                await localService.WarmAsync(local, default);
                checks["warmup_always_reaches_model_not_ocr_or_cache"] = localHandler.Calls == 3 && localHandler.LastBody.Contains("Stay ready.");
            }
            var blockedHandler = new FakeHandler { Status = HttpStatusCode.TooManyRequests };
            var incomplete = new FakeLocalHandler { Body = "{\"message\":{\"content\":\"尚未完成\"},\"done\":false}\n" };
            using (var service = new FastTranslationService(new HttpClient(incomplete), Path.Combine(folder, "incomplete.json")))
            {
                var result = await service.TranslateAsync("There is danger nearby.", false, local, default);
                await service.TranslateAsync("There is danger nearby.", false, local, default);
                checks["incomplete_stream_not_cached"] = result.Text is null && incomplete.Calls == 2;
            }
            var cancelling = new FakeLocalHandler();
            using (var service = new FastTranslationService(new HttpClient(cancelling), Path.Combine(folder, "cancel.json")))
            using (var cts = new CancellationTokenSource())
            {
                var cancelled = false;
                try { await service.TranslateAsync("There is danger nearby.", false, local, cts.Token, _ => cts.Cancel()); }
                catch (OperationCanceledException) { cancelled = true; }
                await service.TranslateAsync("There is danger nearby.", false, local, default);
                checks["superseded_stream_cancelled_and_not_cached"] = cancelled && cancelling.Calls == 2;
            }
            using (var blocked = new FastTranslationService(new HttpClient(blockedHandler), Path.Combine(folder, "blocked.json"), () => "key"))
            {
                var first = await blocked.TranslateAsync("Your army is ready.", false, azure, default);
                await blocked.TranslateAsync("Your skeletons are ready.", false, azure, default);
                checks["rate_limit_stops_more_requests"] = first.Text is null && blockedHandler.Calls == 1;
            }
            var malformed = new FakeHandler { Body = "not JSON" };
            using (var service = new FastTranslationService(new HttpClient(malformed), Path.Combine(folder, "bad.json"), () => "key"))
            {
                var result = await service.TranslateAsync("Your army is ready.", false, azure, default);
                checks["bad_response_is_not_translation"] = result.Text is null;
            }
            var knowledgePath = Path.Combine(folder, "knowledge.json");
            var knowledge = new CoachKnowledgeCache(knowledgePath);
            knowledge.Store("quest", "The hidden story sentence.", new CoachReply(
                "display", "Go to the gate.", "現在要做：前往大門。", [new("gate", "大門")], true));
            var reloadedKnowledge = new CoachKnowledgeCache(knowledgePath);
            checks["model_decision_cache_survives_restart"] = reloadedKnowledge.TryGet(
                "quest", "The hidden story sentence.", "new display", out var cachedReply) &&
                cachedReply.Original == "new display" && cachedReply.TraditionalChinese.Contains("前往");
            checks["model_cache_does_not_store_raw_context"] =
                !File.ReadAllText(knowledgePath).Contains("hidden story sentence", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            // This test owns exactly these flat cache files; never recurse into user data.
            foreach (var file in Directory.GetFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }

    internal static async Task<bool> LiveAsync(string outputPath)
    {
        using var service = new FastTranslationService(cachePath: Path.ChangeExtension(outputPath, ".cache.json"));
        var config = CoachConfig.Load();
        config.OnlineTranslationEnabled = true;
        // Explicit idle budget; a saved config may contain the last combat sample (1).
        config.InferenceThreads = 4;
        var results = new List<object>();
        var passed = true;
        var samples = new (string Text, bool Quest)[]
        {
            ("Deckard Cain\nI need y0ur help.", false),
            ("Head Forward and Search for Leoric.", true),
            ("This effect increases the damage dealt by your summons.", false),
            ("I need your help. Follow me and stay close.", false),
            ("This shield absorbs damage, but it does not restore your health.", false),
            ("Compare the two items before you replace your equipment.", false),
            ("The skill is not ready yet. Wait for the cooldown to end.", false),
            ("This shield absorbs damage, but it does not restore your health.", false)
        };
        foreach (var sample in samples)
        {
            var partialCount = 0;
            var result = await service.TranslateAsync(sample.Text, sample.Quest, config, default, _ => partialCount++);
            passed &= !string.IsNullOrWhiteSpace(result.Text);
            results.Add(new { English = sample.Text, result.Text, result.Source, result.ElapsedMs, result.FirstTextMs, partialCount });
        }
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        return passed && results.Count == samples.Length;
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public int Calls;
        public Uri? LastUri;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = """[{"translations":[{"text":"附近有危險。","to":"zh-Hant"}]}]""";
        public string? LastKey, LastRegion;
        public string LastBody = "";
        public HttpMethod? LastMethod;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++; LastUri = request.RequestUri; LastMethod = request.Method;
            LastKey = request.Headers.GetValues("Ocp-Apim-Subscription-Key").Single();
            LastRegion = request.Headers.GetValues("Ocp-Apim-Subscription-Region").Single();
            LastBody = await request.Content!.ReadAsStringAsync(token);
            return new HttpResponseMessage(Status) { Content = new StringContent(Body) };
        }
    }

    private sealed class FakeLocalHandler : HttpMessageHandler
    {
        public int Calls;
        public Uri? LastUri;
        public string LastBody = "";
        public string Body = "{\"message\":{\"content\":\"這個技能\"},\"done\":false}\n{\"message\":{\"content\":\"造成傷害。\"},\"done\":true,\"done_reason\":\"stop\"}\n";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            LastUri = request.RequestUri;
            LastBody = await request.Content!.ReadAsStringAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Body)
            };
        }
    }
}
