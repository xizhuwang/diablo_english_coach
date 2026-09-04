using System.Drawing.Imaging;
using System.Text.Json;
using EdgeTTS.DotNet;

namespace DiabloEnglishCoach;

internal static class SelfTest
{
    public static async Task<bool> TestPersonalizedTeachingAsync(string outputPath)
    {
        var results = new List<object>();
        var passed = true;
        foreach (var topic in new[] { "toeic", "ic", "game" })
        {
            var anchor = topic == "ic" ? "reset" : topic == "toeic" ? "increase" : "cooldown";
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var lesson = await new CoachService().CreatePersonalizedLessonAsync(
                new LessonPersonalizationContext(topic,
                    "多益核心字: Please confirm the delivery schedule.",
                    "follow=跟隨, damage=傷害", anchor),
                "Increase skill damage. Reset your skills. Wait for the cooldown.", "Stay close.",
                new CoachConfig { InferenceThreads = 4 }, CancellationToken.None);
            results.Add(new
            {
                topic,
                milliseconds = watch.ElapsedMilliseconds,
                lesson?.Title,
                lesson?.English,
                lesson?.Chinese,
                lesson?.SentenceMeaning,
                narration = lesson is null ? null : LessonScript.Narrate(lesson)
            });
            passed &= lesson is not null && lesson.Topic == topic;
        }
        Write(outputPath, new Dictionary<string, object> { ["results"] = results, ["passed"] = passed });
        return passed;
    }

    public static async Task<bool> TestTeachingAsync(string outputPath)
    {
        var results = new List<object>();
        var passed = true;
        foreach (var threads in new[] { 4, 1 })
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var reply = await new CoachService().ExplainAsync("Head Forward and Search for Leoric.",
                new CoachConfig { InferenceThreads = threads }, CancellationToken.None);
            results.Add(new { threads, milliseconds = watch.ElapsedMilliseconds, reply.UsedLocalModel,
                reply.SimpleEnglish, reply.TraditionalChinese, narration = CoachForm.Narration(reply), reply.Notice });
            passed &= reply.UsedLocalModel;
        }
        Write(outputPath, new Dictionary<string, object> { ["results"] = results, ["passed"] = passed });
        return passed;
    }
    public static async Task<bool> TestQuestCoachAsync(string outputPath)
    {
        var checks = new Dictionary<string, object>();
        try
        {
            var reply = await new CoachService().GuideQuestAsync(
                "The Risen Dead. Head to Ashwold Cemetery.",
                string.Empty,
                new CoachConfig(),
                CancellationToken.None,
                includeBuildTip: true);
            checks["used_local_model"] = reply.UsedLocalModel;
            checks["simple_english"] = reply.SimpleEnglish;
            checks["traditional_chinese"] = reply.TraditionalChinese;
            checks["advice"] = reply.Advice ?? string.Empty;
            checks["keywords"] = reply.Keywords.Select(item => new { item.Word, item.Meaning }).ToArray();

            var passed = reply.UsedLocalModel &&
                         reply.TraditionalChinese.StartsWith("現在要做：", StringComparison.Ordinal) &&
                         reply.SimpleEnglish.Length > 0 &&
                         !string.IsNullOrWhiteSpace(reply.Advice);
            checks["passed"] = passed;
            Write(outputPath, checks);
            return passed;
        }
        catch (Exception exception)
        {
            checks["passed"] = false;
            checks["error"] = exception.ToString();
            Write(outputPath, checks);
            return false;
        }
    }

    public static async Task<bool> TestScreenshotRegionsAsync(string imagePath, string outputPath)
    {
        var result = new Dictionary<string, object>();
        try
        {
            using var screenshot = new Bitmap(imagePath);
            var config = new CoachConfig();
            using var questCrop = CropNormalized(
                screenshot, config.QuestRegionX, config.QuestRegionY,
                config.QuestRegionWidth, config.QuestRegionHeight);
            using var questPrepared = CaptureService.PrepareForOcr(questCrop);
            var ocr = new OcrService();
            var questText = await ocr.RecognizeAsync(questPrepared, CancellationToken.None);
            var questFound = OcrService.LooksLikeEnglishSubtitle(questText) &&
                             (questText.Contains("head", StringComparison.OrdinalIgnoreCase) ||
                              questText.Contains("talk", StringComparison.OrdinalIgnoreCase) ||
                              questText.Contains("defeat", StringComparison.OrdinalIgnoreCase) ||
                              questText.Contains("find", StringComparison.OrdinalIgnoreCase) ||
                              questText.Contains("watch", StringComparison.OrdinalIgnoreCase) ||
                              questText.Contains("cemetery", StringComparison.OrdinalIgnoreCase));
            result["quest_text"] = questText;
            result["quest_objective_found"] = questFound;
            result["passed"] = questFound;
            Write(outputPath, result);
            return questFound;
        }
        catch (Exception exception)
        {
            result["passed"] = false;
            result["error"] = exception.ToString();
            Write(outputPath, result);
            return false;
        }
    }

    public static bool CaptureUiPreview(string outputPath)
    {
        try
        {
            // The preview process is terminated immediately by Program after the
            // bitmap is saved, so avoid WinForms/SAPI teardown influencing this
            // layout-only test.
            var form = new CoachForm(previewMode: true);
            form.Show();
            Application.DoEvents();
            var passed = form.VerifyUiAndLoadDemo();
            using var bitmap = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory);
            bitmap.Save(outputPath, ImageFormat.Png);
            return passed;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> GenerateVoiceSampleAsync(string outputPath, string voice, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory);
            var request = new Communicate(text, voice: voice, rate: "+18%", pitch: "+4Hz");
            await request.SaveAsync(outputPath);
            return File.Exists(outputPath) && new FileInfo(outputPath).Length > 1024;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> RunAsync(string outputPath)
    {
        var checks = new Dictionary<string, object>();
        try
        {
            using var source = new Bitmap(1200, 240, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(source))
            using (var font = new Font("Arial", 44))
            {
                graphics.Clear(Color.Black);
                graphics.DrawString("Follow Deckard Cain to Ashwold Cemetery", font, Brushes.White, 20, 40);
                graphics.DrawString("Defeat the undead and find the shard", font, Brushes.White, 20, 120);
            }

            using var prepared = CaptureService.PrepareForOcr(source);
            var ocr = new OcrService();
            var recognized = await ocr.RecognizeAsync(prepared, CancellationToken.None);
            checks["ocr_language"] = ocr.RecognizerLanguage;
            checks["ocr_text"] = recognized;
            checks["ocr_looks_english"] = OcrService.LooksLikeEnglishSubtitle(recognized);
            checks["ocr_key_name_found"] = recognized.Contains("Deckard", StringComparison.OrdinalIgnoreCase) ||
                                            recognized.Contains("Ashwold", StringComparison.OrdinalIgnoreCase);

            var config = new CoachConfig { OllamaUrl = "http://127.0.0.1:1", Model = "qwen3.5:2b-q4_K_M" };
            var fallback = await new CoachService().ExplainAsync(
                "Follow Deckard Cain to Ashwold Cemetery and defeat the undead.", config, CancellationToken.None);
            checks["fallback_works"] = !fallback.UsedLocalModel && fallback.Keywords.Count > 0;
            checks["fallback_keywords"] = fallback.Keywords.Select(item => item.Word).ToArray();

            var questFallback = await new CoachService().GuideQuestAsync(
                "The Risen Dead. Head to Ashwold Cemetery.", string.Empty, config, CancellationToken.None);
            checks["quest_fallback_works"] = !questFallback.UsedLocalModel &&
                                              questFallback.Original.StartsWith("QUEST ·", StringComparison.Ordinal) &&
                                              questFallback.Keywords.Any(item => item.Word == "cemetery");

            var parsed = CoachService.ParseModelReply(
                "Defeat the undead.",
                """{"simple_english":"Beat the undead.","traditional_chinese":"擊敗不死族。","keywords":[{"word":"defeat","meaning":"擊敗"},{"word":"future boss","meaning":"未來首領"}]}""");
            checks["model_json_parses"] = parsed.UsedLocalModel && parsed.TraditionalChinese == "擊敗不死族。";
            checks["unknown_keyword_filtered"] = parsed.Keywords.Count == 1 && parsed.Keywords[0].Word == "defeat";

            using var loadMonitor = new AdaptiveLoadMonitor();
            var sampledLoad = loadMonitor.Sample(coachBusy: false);
            checks["adaptive_load_sample_valid"] = sampledLoad.CpuPercent is >= 0 and <= 100 && sampledLoad.ActionKeyIdleMs >= 0;
            checks["adaptive_load_defers_input"] = AdaptiveLoadMonitor.ShouldDefer(20, 500, false);
            checks["adaptive_load_defers_high_cpu"] = AdaptiveLoadMonitor.ShouldDefer(85, 5000, false);
            checks["adaptive_load_resumes_when_idle"] = !AdaptiveLoadMonitor.ShouldDefer(30, 5000, false);
            checks["space_is_dialogue_not_action"] = !KeyboardActivityMonitor.IsActionKey(KeyboardActivityMonitor.SpaceVirtualKey);
            checks["wasd_is_action"] = KeyboardActivityMonitor.IsActionKey((int)Keys.W) &&
                                        KeyboardActivityMonitor.IsActionKey((int)Keys.A) &&
                                        KeyboardActivityMonitor.IsActionKey((int)Keys.S) &&
                                        KeyboardActivityMonitor.IsActionKey((int)Keys.D);
            var screen = new Rectangle(0, 0, 1920, 1040);
            checks["old_top_position_hits_enemy_hud"] = CoachForm.OverlapsEnemyHud(
                new Rectangle(346, 10, 1229, 158), screen);
            checks["new_bottom_position_avoids_enemy_hud"] = !CoachForm.OverlapsEnemyHud(
                new Rectangle(346, 872, 1229, 158), screen);

            var advised = BuildAdvisor.AppendTip(parsed, new CoachConfig(), "Head to Ashwold Cemetery.");
            checks["advice_preserves_quest_translation"] = advised.TraditionalChinese == parsed.TraditionalChinese &&
                                                          !string.IsNullOrWhiteSpace(advised.Advice);
            checks["advice_can_be_disabled"] = BuildAdvisor.AppendTip(parsed,
                new CoachConfig { BuildTipsEnabled = false }, "quest") == parsed;
            checks["class_preference_changes_advice"] =
                !BuildAdvisor.GetTips("Necromancer", "Summons").SequenceEqual(BuildAdvisor.GetTips("Necromancer", "Survival"));

            var planner = new IdleLessonPlanner(persistGenerated: false);
            var lessonConfig = new CoachConfig();
            planner.Reset(0);
            var lessonInterval = IdleLessonPlanner.IntervalMs;
            checks["idle_requires_quiet_period"] = planner.TryNext(lessonInterval, true, lessonConfig) is null;
            checks["idle_teaches_after_quiet_period"] = planner.TryNext(lessonInterval + IdleLessonPlanner.QuietWindowMs, true, lessonConfig) is not null;
            checks["idle_no_burst"] = planner.TryNext(lessonInterval + IdleLessonPlanner.QuietWindowMs + 1, true, lessonConfig) is null;
            var busyAt = lessonInterval + IdleLessonPlanner.QuietWindowMs + lessonInterval;
            checks["busy_blocks_overdue_lesson"] = planner.TryNext(busyAt, false, lessonConfig) is null;
            checks["busy_resets_quiet_period"] = planner.TryNext(busyAt + 1000, true, lessonConfig) is null;
            var recoveredAt = busyAt + 1000 + IdleLessonPlanner.QuietWindowMs;
            checks["idle_recovers_after_combat"] = planner.TryNext(recoveredAt, true, lessonConfig) is not null;
            planner.ObserveLesson(recoveredAt + 1000, new[]
            {
                new KeywordCard("head to", "前往"), new KeywordCard("follow", "跟隨"),
                new KeywordCard("defeat", "擊敗")
            });
            checks["new_quest_delays_idle_lesson"] = planner.TryNext(recoveredAt + 1000 + lessonInterval, true, lessonConfig) is null;
            var lessons = new List<IdleLesson>();
            var curriculumStart = recoveredAt + 1000 + lessonInterval + IdleLessonPlanner.QuietWindowMs;
            for (var now = curriculumStart; now < curriculumStart + 28 * lessonInterval; now += lessonInterval)
            {
                if (planner.TryNext(now, true, lessonConfig) is { } lesson)
                    lessons.Add(lesson);
            }
            checks["idle_repeats_seen_words"] = lessons.Any(lesson => lesson.English == "head to");
            checks["idle_includes_build_preparation"] = lessons.Any(lesson => lesson.Title.Contains("配裝"));
            checks["idle_no_unconnected_toeic"] = lessons.All(lesson => lesson.Topic != "toeic");
            checks["idle_no_unconnected_digital_ic"] = lessons.All(lesson => lesson.Topic != "ic");
            checks["idle_includes_game_english"] = lessons.Any(lesson => lesson.Topic == "game");
            checks["idle_varied_lessons"] = lessons.Select(lesson => lesson.English).Distinct().Count() >= 7;
            checks["idle_avoids_short_term_repetition"] = !lessons.Zip(lessons.Skip(1))
                .Any(pair => pair.First.English == pair.Second.English);
            var generatedPlanner = new IdleLessonPlanner(persistGenerated: false);
            generatedPlanner.ObserveGameContext("Wait for the cooldown.", "");
            var generated = new IdleLesson("AI 個人化 · 遊戲",
                "The cooldown ends after a few seconds.",
                "cooldown 是冷卻時間。after 後面接經過的時間。", "game", "冷卻時間會在幾秒後結束。", "cooldown");
            checks["personalized_lesson_is_queued"] = generatedPlanner.AddPersonalizedLesson(generated);
            checks["personalized_exact_duplicate_rejected"] = !generatedPlanner.AddPersonalizedLesson(generated);
            generatedPlanner.Reset(0);
            checks["personalized_lesson_has_priority"] = generatedPlanner.TryNext(
                IdleLessonPlanner.IntervalMs, true, lessonConfig, requireQuietWindow: false)?.English == generated.English;

            var parsedPersonalized = CoachService.ParsePersonalizedLesson(
                "AI 個人化 · 多益", "toeic", "confirm, schedule",
                """{"english":"Please confirm the revised schedule.","sentence_meaning":"請確認修訂後的時程。","usage":"confirm 後面接要確認的事情。","keyword":"confirm"}""");
            checks["personalized_model_json_parses"] = parsedPersonalized is
                { Topic: "toeic", English: "Please confirm the revised schedule." };
            var script = LessonScript.Narrate(parsedPersonalized! with { AnchorWord = "confirm" });
            checks["lesson_has_context_example_translation_usage"] = script.Contains("遊戲裡的 confirm") &&
                script.IndexOf("例句：") < script.IndexOf("整句意思是：") &&
                script.Contains("請確認修訂後的時程") && script.Contains("confirm 後面接");
            checks["curriculum_all_examples_have_meanings"] = lessons.Where(l => l.Title is "多益核心字" or "數位 IC 面試字" or "遊戲英文")
                .All(l => !string.IsNullOrWhiteSpace(l.SentenceMeaning));
            checks["context_free_model_fragment_rejected"] = CoachService.ParsePersonalizedLesson("AI", "toeic", "confirm",
                """{"english":"Please confirm the schedule.","keyword":"confirm"}""") is null;
            checks["personalized_disallows_unapproved_term"] = CoachService.ParsePersonalizedLesson(
                "AI 個人化 · 多益", "toeic", "confirm, schedule",
                """{"english":"Please purchase the item.","traditional_chinese":"這是購買的意思。","keyword":"purchase","meaning":"購買"}""") is null;

            checks["cooldown_is_not_circuit_timing"] = !GameContextLessons.Allows("cooldown", "ic");
            checks["cooldown_charging_error_rejected"] = !GameContextLessons.SafeExample("cooldown",
                "Wait for the cooldown.", "等待充能完成才能行動。");
            checks["word_boundary_not_partial_match"] = !GameContextLessons.ContainsWord("The estate is empty.", "reset");
            checks["inflected_game_word_matches"] = GameContextLessons.Find("This effect increases damage.", "").Any(l => l.Word == "increase");
            var confirmLink = GameContextLessons.Find("Confirm your choice.", "").Single(l => l.Word == "confirm");
            var toeicExtension = GameContextLessons.Create(confirmLink, true);
            checks["toeic_extension_is_complete_sentence"] = toeicExtension.English == "Please confirm the delivery schedule." &&
                toeicExtension.SentenceMeaning == "請確認交貨時程。" && toeicExtension.Topic == "toeic";
            checks["toeic_500_750_bank_expanded"] = GameContextLessons.Find(
                "Complete, receive, select, provide, replace, reduce, collect, purchase, and improve.", "").Count >= 9;
            checks["unrelated_model_lesson_rejected"] = !generatedPlanner.AddPersonalizedLesson(parsedPersonalized! with { AnchorWord = "confirm" });
            foreach (var (word, topic) in new[] { ("increase", "toeic"), ("reset", "ic") })
            {
                var contextual = new IdleLessonPlanner(persistGenerated: false);
                contextual.ObserveGameContext(word == "reset" ? "Reset your skills." : "Increase skill damage.", "");
                var sequence = Enumerable.Range(1, 24).Select(i => contextual.TryNext(i * 1000, true,
                    new CoachConfig { BuildTipsEnabled = false }, false)!).ToArray();
                checks[$"{topic}_starts_with_game_meaning"] = sequence[0].Topic == "game" && sequence[0].AnchorWord == word;
                var extensionTurns = sequence.Select((l, i) => (l, i)).Where(x => x.l.Topic == topic).Select(x => x.i).ToArray();
                checks[$"{topic}_natural_extension_exists"] = extensionTurns.Length > 0;
                checks[$"{topic}_extensions_spaced"] = !extensionTurns.Zip(extensionTurns.Skip(1)).Any(x => x.Second - x.First < 4);
            }
            var stale = new IdleLessonPlanner(persistGenerated: false);
            stale.ObserveGameContext("Wait for the cooldown.", "");
            stale.AddPersonalizedLesson(generated);
            stale.ObserveGameContext("Follow the guide.", "");
            checks["context_change_discards_queued_lesson"] = stale.TryNext(1000, true, lessonConfig, false)?.English != generated.English;
            checks["speaking_skips_unrelated_previous_lesson"] = !stale.CanPractice(generated);
            checks["no_context_skips_model_work"] = !new IdleLessonPlanner(persistGenerated: false).NeedsPersonalizedLesson;
            checks["old_cached_lesson_boilerplate_removed"] = LessonScript.Narrate(new IdleLesson("遊戲英文", "Return to the gate.",
                "return to 表示回到某處。這是例句，不是新的任務。實際效果以遊戲為準。", "game", "回到大門。", "return"))
                is var direct && direct.Contains("例句：Return to the gate.") && direct.Contains("回到大門") &&
                !direct.Contains("不是新的任務") && !direct.Contains("為準");
            const string concreteLimit = "還沒看到你的裝備屬性。未解鎖就先保留現有配置。equip 是裝備，不是只放進背包。";
            checks["direct_speech_keeps_real_conditions"] = SpokenStyle.Clean(concreteLimit) == concreteLimit;
            checks["game_lessons_no_boilerplate"] = GameContextLessons.Find(
                "Return to the gate. Reset your skills. Head to the gate. Follow the guide. Head forward. Defeat the enemy. Equip the item. Upgrade the item. Leave the dungeon.", "")
                .Select(l => LessonScript.Narrate(GameContextLessons.Create(l, false)))
                .All(s => !s.Contains("不是新") && !s.Contains("不是要") && !s.Contains("只是示範"));

            var englishOnly = new IdleLessonPlanner(persistGenerated: false);
            var noBuild = new CoachConfig { BuildTipsEnabled = false };
            englishOnly.Reset(0);
            englishOnly.TryNext(0, true, noBuild);
            checks["idle_honors_disabled_build_tips"] = Enumerable.Range(1, 9)
                .Select(i => englishOnly.TryNext(IdleLessonPlanner.QuietWindowMs + i * lessonInterval, true, noBuild))
                .All(lesson => lesson is not null && !lesson.Title.StartsWith("一般配裝"));

            foreach (var pair in await SpeakingSelfTest.PolicyAsync()) checks[pair.Key] = pair.Value;
            foreach (var pair in await SpeechTranscriptSelfTest.RunAsync()) checks[pair.Key] = pair.Value;
            foreach (var pair in ExperienceSelfTest.Run()) checks[pair.Key] = pair.Value;
            foreach (var pair in await ParagraphSpeechSelfTest.RunAsync()) checks[pair.Key] = pair.Value;
            foreach (var pair in await BuildGuideSelfTest.RunAsync()) checks[pair.Key] = pair.Value;
            await FastTranslationSelfTest.AddChecksAsync(checks);
            var passed = checks.Values.OfType<bool>().All(value => value);
            checks["passed"] = passed;
            Write(outputPath, checks);
            return passed;
        }
        catch (Exception exception)
        {
            checks["passed"] = false;
            checks["error"] = exception.ToString();
            Write(outputPath, checks);
            return false;
        }
    }

    private static void Write(string outputPath, Dictionary<string, object> checks)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Bitmap CropNormalized(Bitmap source, double x, double y, double width, double height)
    {
        var rectangle = new Rectangle(
            (int)Math.Round(source.Width * x),
            (int)Math.Round(source.Height * y),
            Math.Max(1, (int)Math.Round(source.Width * width)),
            Math.Max(1, (int)Math.Round(source.Height * height)));
        rectangle = Rectangle.Intersect(rectangle, new Rectangle(Point.Empty, source.Size));
        return source.Clone(rectangle, PixelFormat.Format32bppArgb);
    }
}
