namespace DiabloEnglishCoach;

// Complete examples, not disconnected vocabulary lists. Every script is sent
// as ONE speech paragraph so a topic cannot change halfway through its meaning.
internal static class LessonScript
{
    private static readonly Dictionary<string, string> Meanings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Please confirm the delivery schedule."] = "請確認交貨時程。",
        ["The meeting has been postponed."] = "會議已經延期了。",
        ["Employees must comply with the policy."] = "員工必須遵守這項政策。",
        ["The position requires relevant experience."] = "這個職缺需要相關經驗。",
        ["We appreciate your prompt response."] = "我們感謝您的迅速回覆。",
        ["The device is temporarily unavailable."] = "這個裝置暫時無法使用。",
        ["Sales increased significantly this quarter."] = "這一季的銷售額顯著增加。",
        ["Submit the form before the deadline."] = "請在截止期限前提交表單。",
        ["Explain the difference between latency and throughput."] = "請解釋延遲和吞吐量之間的差別。",
        ["A flip-flop stores one bit of state."] = "一個正反器可以儲存一個位元的狀態。",
        ["Setup time is checked before the clock edge."] = "建立時間檢查的是時脈邊緣之前的資料穩定時間。",
        ["Hold time is checked after the clock edge."] = "保持時間檢查的是時脈邊緣之後的資料穩定時間。",
        ["Use nonblocking assignments in sequential logic."] = "在循序邏輯中使用非阻塞賦值。",
        ["Combinational logic should avoid unintended latches."] = "組合邏輯應避免產生非預期的鎖存器。",
        ["A synchronizer reduces metastability risk."] = "同步器可以降低亞穩態造成問題的風險。",
        ["Pipelining can improve the maximum clock frequency."] = "管線化可以提高最高時脈頻率。",
        ["Head to the marked area."] = "前往標記的區域。",
        ["Wait for the cooldown."] = "等待冷卻時間結束。",
        ["This effect increases skill damage."] = "這個效果會增加技能傷害。",
        ["Keep a defensive option."] = "保留一個防禦手段。",
        ["Pick up the loot."] = "撿起戰利品。",
        ["The skill deals area damage."] = "這個技能會造成範圍傷害。",
        ["Compare one item at a time."] = "一次比較一件物品。",
        ["Interact with the object."] = "與這個物件互動。",
        ["Latency is the time required to produce one result."] = "延遲是產生一個結果所需要的時間。",
        ["Throughput measures how many results are produced per unit time."] = "吞吐量衡量的是每單位時間產生多少個結果。",
        ["A flip-flop samples data on an active clock edge."] = "正反器會在有效時脈邊緣取樣資料。",
        ["The register stores the current state of the controller."] = "暫存器儲存控制器目前的狀態。",
        ["Data must be stable before the edge for the setup time."] = "資料在時脈邊緣之前，必須維持穩定至少一段建立時間。",
        ["Data must remain stable after the edge for the hold time."] = "資料在時脈邊緣之後，必須持續穩定至少一段保持時間。",
        ["The flip-flop samples its input at the clock edge."] = "正反器會在時脈邊緣取樣輸入資料。",
        ["Use a nonblocking assignment in clocked sequential logic."] = "在時脈驅動的循序邏輯中使用非阻塞賦值。",
        ["Sequential logic stores state between clock cycles."] = "循序邏輯會在不同時脈週期之間保存狀態。",
        ["Combinational logic depends only on its current inputs."] = "組合邏輯的輸出只取決於目前的輸入。",
        ["Incomplete combinational assignments can infer a latch."] = "組合邏輯的賦值不完整時，可能推導出鎖存器。",
        ["A synchronizer reduces the probability of metastability propagation."] = "同步器降低亞穩態傳播到後續電路的機率。",
        ["Metastability risk increases when timing requirements are violated."] = "違反時序要求時，亞穩態風險會增加。",
        ["A pipeline trades additional latency for higher throughput."] = "管線化可以用額外延遲換取更高的吞吐量。",
        ["The critical path limits the maximum clock frequency."] = "關鍵路徑限制了最高時脈頻率。",
        ["Reset places the design in a known state."] = "重置會把電路設計帶到一個已知的狀態。",
        ["Static timing analysis checks paths against timing constraints."] = "靜態時序分析會依照時序限制檢查路徑。",
        ["Synthesis maps RTL code into a gate-level representation."] = "合成會把 RTL 程式碼轉換成邏輯閘層級的表示方式。",
        ["A timing constraint describes a required timing relationship."] = "時序限制描述了設計必須滿足的時序關係。",
        ["Verification checks whether the design meets its specification."] = "驗證會檢查設計是否符合規格。"
    };

    internal static IdleLesson Complete(IdleLesson lesson) =>
        Meanings.TryGetValue(lesson.English, out var meaning)
            ? lesson with { SentenceMeaning = meaning } : lesson;

    internal static string Narrate(IdleLesson source)
    {
        var lesson = Complete(source);
        if (lesson.Topic == "review")
            return SpokenStyle.Clean($"複習剛才遇到的單字。{lesson.Chinese}");
        if (string.IsNullOrWhiteSpace(lesson.SentenceMeaning))
            return SpokenStyle.Clean($"{lesson.English}。{lesson.Chinese}");
        var context = GameContextLessons.Introduction(lesson);
        return SpokenStyle.Clean($"{context}例句：{lesson.English}。整句意思是：{lesson.SentenceMeaning}用法重點：{lesson.Chinese}");
    }
}
