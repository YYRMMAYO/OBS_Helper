using System.Collections.Concurrent;
using System.IO;
using OBS_Helper.Wpf.Services.Mobile;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OBS_Helper.Wpf.Services.Chat;

public enum ChatPlatform { Bilibili, Twitch, YouTube }

public sealed class ChatMessage
{
    public ChatPlatform Platform { get; init; }
    public string Room { get; init; } = "";
    public string UserId { get; init; } = "";
    public string UserName { get; init; } = "";
    public string Text { get; init; } = "";
    public string Kind { get; init; } = "chat"; // chat / gift / superchat / enter / notice
    public long Value { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.Now;
    public bool IsModerated { get; init; }
}

/// <summary>
/// 弹幕归一化 / 过滤 / 去重 / 限流（C01/C09）。
/// 只保存纯文本，不保存平台原始 HTML、cookie 或登录态。
/// </summary>
public static class ChatCore
{
    public const int MaxTextLength = 500;
    public const int UiPerSecondPerPlatform = 50;
    public const int OverlayPerSecond = 30;

    private static readonly Regex UrlPattern = new(@"https?://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EmojiPattern = new(@"[\uD800-\uDBFF][\uDC00-\uDFFF]|[\u2600-\u27BF]", RegexOptions.Compiled);

    public static string NormalizeText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsControl(ch) && ch is not '\t') continue;
            if (ch is '\u200B' or '\u200C' or '\u200D' or '\uFEFF' or '\u202E') continue;
            sb.Append(ch);
        }
        var cleaned = sb.ToString().Trim();
        if (cleaned.Length > MaxTextLength) cleaned = cleaned[..MaxTextLength];
        return cleaned;
    }

    /// <summary>统一构造消息；空文本/纯空白返回 null（不显示）。</summary>
    public static ChatMessage? Create(ChatPlatform platform, string room, string userId, string userName,
        string? text, string kind = "chat", long value = 0)
    {
        var normalized = NormalizeText(text);
        if (normalized.Length == 0) return null;
        return new ChatMessage
        {
            Platform = platform,
            Room = room,
            UserId = userId ?? "",
            UserName = NormalizeText(userName).Length == 0 ? "anonymous" : NormalizeText(userName),
            Text = normalized,
            Kind = kind,
            Value = value,
        };
    }

    /// <summary>越界统计：同一平台每秒超过上限就丢弃并计数，不阻塞接收线程。</summary>
    public sealed class RateWindow
    {
        private readonly int _limit;
        private readonly Func<DateTimeOffset> _clock;
        private readonly Queue<long> _ticks = new();
        private readonly object _gate = new();
        public long Dropped { get; private set; }

        public RateWindow(int limit, Func<DateTimeOffset>? clock = null)
        {
            _limit = Math.Max(1, limit);
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        public bool TryAccept()
        {
            lock (_gate)
            {
                var now = _clock().UtcTicks;
                var cutoff = now - TimeSpan.TicksPerSecond;
                while (_ticks.Count > 0 && _ticks.Peek() <= cutoff) _ticks.Dequeue();
                if (_ticks.Count >= _limit) { Dropped++; return false; }
                _ticks.Enqueue(now);
                return true;
            }
        }
    }

    /// <summary>去重窗口：相同用户 + 相同文本在窗口内只保留一次（防刷屏/重连重放）。</summary>
    public sealed class Deduper
    {
        private readonly TimeSpan _window;
        private readonly int _capacity;
        private readonly Dictionary<string, long> _seen = new(StringComparer.Ordinal);
        private readonly Queue<string> _order = new();
        private readonly Func<DateTimeOffset> _clock;

        public Deduper(TimeSpan? window = null, int capacity = 500, Func<DateTimeOffset>? clock = null)
        {
            _window = window ?? TimeSpan.FromSeconds(5);
            _capacity = capacity;
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        public bool IsDuplicate(ChatMessage message)
        {
            var key = message.Platform + "\n" + message.UserId + "\n" + message.Text;
            var now = _clock().UtcTicks;
            if (_seen.TryGetValue(key, out var at) && now - at < _window.Ticks) return true;
            _seen[key] = now;
            _order.Enqueue(key);
            while (_order.Count > _capacity)
            {
                var old = _order.Dequeue();
                _seen.Remove(old);
            }
            return false;
        }
    }

    /// <summary>弹幕渲染前的纯文本化：URL 与表情可被替换（TTS / 上屏共用）。</summary>
    public static string StripLinksAndEmoji(string text)
        => EmojiPattern.Replace(UrlPattern.Replace(text, ""), "").Trim();

    public static bool ContainsLink(string text) => UrlPattern.IsMatch(text);
}

/// <summary>敏感词 / 刷屏预警（C09）：本地词表 + 频率检测，只提醒、不自动处理。</summary>
public static class ChatModerationCore
{
    public sealed class Alert
    {
        public string UserName { get; init; } = "";
        public string Reason { get; init; } = "";
        public string Text { get; init; } = "";
        public DateTimeOffset At { get; init; } = DateTimeOffset.Now;
    }

    public static readonly string[] DefaultWords =
    {
        "加微信", "代练", "刷单", "返利", "私信我", "免费送", "扫码领", "博彩", "下注", "开挂",
    };

    /// <summary>命中敏感词或频率超阈值时返回告警（同一用户 10 秒内 8 条视为刷屏）。</summary>
    public sealed class Detector
    {
        private readonly HashSet<string> _words;
        private readonly Dictionary<string, Queue<long>> _frequency = new(StringComparer.Ordinal);
        private readonly Func<DateTimeOffset> _clock;
        public List<Alert> Alerts { get; } = new();

        public Detector(IEnumerable<string>? words = null, Func<DateTimeOffset>? clock = null)
        {
            _words = new HashSet<string>(words ?? DefaultWords, StringComparer.OrdinalIgnoreCase);
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        public void AddWord(string word)
        {
            if (!string.IsNullOrWhiteSpace(word)) _words.Add(word.Trim());
        }

        public Alert? Check(ChatMessage message)
        {
            var hit = _words.FirstOrDefault(w => message.Text.Contains(w, StringComparison.OrdinalIgnoreCase));
            Alert? alert = null;
            if (hit is not null)
                alert = new Alert { UserName = message.UserName, Text = message.Text, Reason = "敏感词：" + hit };

            var now = _clock().UtcTicks;
            if (!_frequency.TryGetValue(message.UserId, out var queue))
            {
                queue = new Queue<long>();
                _frequency[message.UserId] = queue;
            }
            queue.Enqueue(now);
            while (queue.Count > 0 && queue.Peek() < now - TimeSpan.FromSeconds(10).Ticks) queue.Dequeue();
            if (queue.Count >= 8 && alert is null)
                alert = new Alert { UserName = message.UserName, Text = message.Text, Reason = "刷屏" };

            if (alert is not null)
            {
                Alerts.Add(alert);
                if (Alerts.Count > 500) Alerts.RemoveAt(0);
            }
            return alert;
        }

        public string ExportCsv()
        {
            var sb = new StringBuilder("time,user,reason,text\n");
            foreach (var a in Alerts)
                sb.Append(a.At.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                  .Append(Csv(a.UserName)).Append(',').Append(Csv(a.Reason)).Append(',').Append(Csv(a.Text)).Append('\n');
            return sb.ToString();
        }

        private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }
}

/// <summary>
/// B 站弹幕协议（C01）：VarInt 包长、zlib/brotli 压缩包、认证/心跳/消息包。
/// 只复刻公开协议，不复制任何 GPL/第三方源码。
/// </summary>
public static class BilibiliPacketCore
{
    public const int OpHeartbeat = 2;
    public const int OpHeartbeatReply = 3;
    public const int OpMessage = 5;
    public const int OpAuth = 7;
    public const int OpAuthReply = 8;

    public sealed class Packet
    {
        public int Operation { get; init; }
        public int Version { get; init; }
        public byte[] Body { get; init; } = Array.Empty<byte>();
    }

    public static bool IsValidRoomId(string? room) => int.TryParse(room, out var id) && id > 0;

    public static string NormalizeRoomId(string? room)
    {
        var digits = new string((room ?? "").Where(char.IsDigit).ToArray());
        return digits.Length is > 0 and <= 10 ? digits : "";
    }

    /// <summary>构造认证包（op=7，JSON body）。token 可空（匿名连接）。</summary>
    public static byte[] BuildAuthPacket(int roomId, string? token = null)
    {
        var json = JsonSerializer.Serialize(new
        {
            uid = 0,
            roomid = roomId,
            protover = 3,
            platform = "web",
            type = 2,
            key = token ?? "",
        });
        return BuildFrame(OpAuth, Encoding.UTF8.GetBytes(json));
    }

    public static byte[] BuildHeartbeatPacket() => BuildFrame(OpHeartbeat, Array.Empty<byte>());

    /// <summary>16 字节头：包长(4) 头长(2)=16 版本(2) 操作(4) 序号(4)。</summary>
    public static byte[] BuildFrame(int operation, byte[] body, int version = 1, int sequence = 1)
    {
        var frame = new byte[16 + body.Length];
        WriteBigEndian(frame, 0, frame.Length);
        frame[4] = 0; frame[5] = 16;
        frame[6] = (byte)(version >> 8); frame[7] = (byte)version;
        WriteBigEndian(frame, 8, operation);
        WriteBigEndian(frame, 12, sequence);
        body.CopyTo(frame, 16);
        return frame;
    }

    /// <summary>拆包；返回 null 表示数据不完整。超过 10 MB 或长度非法抛异常。</summary>
    public static List<Packet>? SplitPackets(ReadOnlySpan<byte> data)
    {
        var packets = new List<Packet>();
        var offset = 0;
        while (offset + 16 <= data.Length)
        {
            var length = ReadBigEndian(data, offset);
            var headerLength = (data[offset + 4] << 8) | data[offset + 5];
            var version = (data[offset + 6] << 8) | data[offset + 7];
            var operation = ReadBigEndian(data, offset + 8);
            if (length < 16 || length > 10 * 1024 * 1024 || headerLength < 16 || headerLength > length) return null;
            if (offset + length > data.Length) return null;
            var body = data.Slice(offset + headerLength, length - headerLength).ToArray();
            packets.Add(new Packet { Operation = operation, Version = version, Body = body });
            offset += length;
        }
        return offset == data.Length ? packets : null;
    }

    /// <summary>解压数据包：版本 2 = zlib，3 = brotli，0/1 = 原样。</summary>
    public static Packet Decompress(Packet packet)
    {
        if (packet.Version is not (2 or 3)) return packet;
        try
        {
            using var input = new MemoryStream(packet.Body);
            using Stream decompressor = packet.Version == 2 ? new ZLibStream(input, CompressionMode.Decompress) : new BrotliStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            decompressor.CopyTo(output);
            return new Packet { Operation = packet.Operation, Version = 0, Body = output.ToArray() };
        }
        catch (Exception)
        {
            return packet;
        }
    }

    /// <summary>把 op=5 的 body 解析成归一化消息（可包含多条 JSON，以 NUL 分隔）。</summary>
    public static IEnumerable<ChatMessage> ParseMessages(string room, byte[] body)
    {
        var text = Encoding.UTF8.GetString(body);
        foreach (var chunk in text.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(chunk); }
            catch (JsonException) { continue; }
            using (doc)
            {
                var message = ParseOne(room, doc.RootElement);
                if (message is not null) yield return message;
            }
        }
    }

    private static ChatMessage? ParseOne(string room, JsonElement root)
    {
        var cmd = root.TryGetProperty("cmd", out var c) ? c.GetString() ?? "" : "";
        if (!root.TryGetProperty("info", out var info) && !root.TryGetProperty("data", out info)) return null;
        if (cmd.StartsWith("DANMU_MSG", StringComparison.Ordinal))
        {
            if (info.ValueKind != JsonValueKind.Array || info.GetArrayLength() < 3) return null;
            // 公开协议：info[1] = 弹幕文本，info[2] = [uid, uname, ...]（登录相关字段可缺省）
            var text = info[1].ValueKind == JsonValueKind.String ? info[1].GetString() : "";
            var user = info[2];
            var name = user.ValueKind == JsonValueKind.Array && user.GetArrayLength() > 1 && user[1].ValueKind == JsonValueKind.String
                ? user[1].GetString() ?? "" : "";
            var uid = user.ValueKind == JsonValueKind.Array && user.GetArrayLength() > 0 ? user[0].GetRawText() : "";
            return ChatCore.Create(ChatPlatform.Bilibili, room, uid, name, text);
        }
        if (cmd.StartsWith("SEND_GIFT", StringComparison.Ordinal))
        {
            if (info.ValueKind != JsonValueKind.Object) return null;
            var user = info.TryGetProperty("uname", out var u) ? u.GetString() ?? "" : "";
            var action = info.TryGetProperty("action", out var a) ? a.GetString() ?? "投喂" : "投喂";
            var gift = info.TryGetProperty("giftName", out var g) ? g.GetString() ?? "" : "";
            var num = info.TryGetProperty("num", out var n) && n.TryGetInt64(out var nv) ? nv : 1;
            var price = info.TryGetProperty("price", out var p) && p.TryGetInt64(out var pv) ? pv * num : 0;
            return ChatCore.Create(ChatPlatform.Bilibili, room, user, user, $"{action} {gift} ×{num}", "gift", price);
        }
        if (cmd.StartsWith("SUPER_CHAT_MESSAGE", StringComparison.Ordinal))
        {
            if (info.ValueKind != JsonValueKind.Object) return null;
            var user = info.TryGetProperty("user_info", out var ui) && ui.TryGetProperty("uname", out var un) ? un.GetString() ?? "" : "";
            var text = info.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            var price = info.TryGetProperty("price", out var p) && p.TryGetInt64(out var pv) ? pv : 0;
            return ChatCore.Create(ChatPlatform.Bilibili, room, user, user, text, "superchat", price);
        }
        if (cmd.StartsWith("INTERACT_WORD", StringComparison.Ordinal))
        {
            if (info.ValueKind != JsonValueKind.Object) return null;
            var user = info.TryGetProperty("uname", out var u) ? u.GetString() ?? "" : "";
            var msgType = info.TryGetProperty("msg_type", out var mt) && mt.TryGetInt32(out var mv) ? mv : 1;
            // B 站公开协议：1=进入、2=关注、3=分享、4=特别关注。关注单独记为 follow，
            // 复盘页才能把「新增关注」从弹幕里自动回填出来。
            var isFollow = msgType is 2 or 4;
            return ChatCore.Create(ChatPlatform.Bilibili, room, user, user,
                isFollow ? "关注了直播间" : "进入直播间", isFollow ? "follow" : "enter");
        }
        return null;
    }

    /// <summary>心跳回复里的直播间人气值（op=3，4 字节大端；部分版本为 JSON）。解析失败返回 0。</summary>
    public static int ParsePopularity(ReadOnlySpan<byte> body)
    {
        if (body.Length >= 4)
        {
            var value = ReadBigEndian(body, 0);
            if (value > 0) return value;
        }
        if (body.Length == 0) return 0;
        try
        {
            using var doc = JsonDocument.Parse(body.ToArray());
            var root = doc.RootElement;
            if (root.TryGetProperty("popularity", out var p) && p.TryGetInt32(out var pv)) return Math.Max(0, pv);
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("popularity", out var dp) && dp.TryGetInt32(out var dpv)) return Math.Max(0, dpv);
        }
        catch (JsonException) { }
        return 0;
    }

    public static string DanmuInfoUrl(int roomId)
        => "https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?id=" + roomId;

    private static int ReadBigEndian(ReadOnlySpan<byte> data, int offset)
        => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}

/// <summary>Twitch IRC over WebSocket（C01）：匿名 justinfan，只读，不要求 OAuth。</summary>
public static class TwitchIrcCore
{
    public const string ServerUrl = "wss://irc-ws.chat.twitch.tv:443";

    public static string NormalizeChannel(string? channel)
    {
        var name = (channel ?? "").Trim().TrimStart('#').ToLowerInvariant();
        if (name.Length is 0 or > 25) return "";
        return name.All(ch => char.IsLetterOrDigit(ch) || ch == '_') ? name : "";
    }

    public static string AnonymousNick(int? seed = null)
        => "justinfan" + (seed ?? Random.Shared.Next(10000, 99999));

    public static IEnumerable<string> JoinCommands(string channel, int? seed = null)
    {
        var name = NormalizeChannel(channel);
        if (name.Length == 0) yield break;
        yield return "NICK " + AnonymousNick(seed);
        yield return "JOIN #" + name;
    }

    /// <summary>把一行 IRC 解析成消息；非 PRIVMSG/USERNOTICE 返回 null。</summary>
    public static ChatMessage? ParseLine(string line, string room)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var rest = line.TrimEnd('\r', '\n');
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        if (rest.StartsWith('@'))
        {
            var space = rest.IndexOf(' ');
            if (space < 0) return null;
            foreach (var pair in rest[1..space].Split(';'))
            {
                var eq = pair.IndexOf('=');
                if (eq > 0) tags[pair[..eq]] = pair[(eq + 1)..];
            }
            rest = rest[(space + 1)..].TrimStart();
        }
        string? prefix = null;
        if (rest.StartsWith(':'))
        {
            var space = rest.IndexOf(' ');
            if (space < 0) return null;
            prefix = rest[1..space];
            rest = rest[(space + 1)..].TrimStart();
        }
        var parts = rest.Split(' ', 3);
        if (parts.Length < 2) return null;
        var command = parts[0];
        if (command is not ("PRIVMSG" or "USERNOTICE")) return null;

        string target;
        string text;
        if (parts.Length == 3 && parts[2].StartsWith(':'))
        {
            target = parts[1];
            text = parts[2][1..];
        }
        else if (command == "USERNOTICE" && parts.Length == 3)
        {
            var sp = parts[2].IndexOf(" :", StringComparison.Ordinal);
            target = parts[1];
            text = sp >= 0 ? parts[2][(sp + 2)..] : (tagOrEmpty(tags, "system-msg"));
        }
        else
        {
            return null;
        }

        var display = tagOrEmpty(tags, "display-name");
        var user = tagOrEmpty(tags, "login");
        if (display.Length == 0) display = user;
        if (display.Length == 0 && prefix is not null)
        {
            var bang = prefix.IndexOf('!');
            display = bang > 0 ? prefix[..bang] : prefix;
        }
        var kind = command == "USERNOTICE" ? "notice" : "chat";
        return ChatCore.Create(ChatPlatform.Twitch, room, user, display, text, kind);
    }

    /// <summary>PING 必须原样回 PONG，否则 4 分钟内被断开。</summary>
    public static string? ResponseFor(string? line)
    {
        if (line is null) return null;
        if (line.StartsWith("PING", StringComparison.Ordinal)) return line.Replace("PING", "PONG");
        return null;
    }

    private static string tagOrEmpty(Dictionary<string, string> tags, string name)
        => tags.TryGetValue(name, out var value) ? value : "";
}

/// <summary>
/// 弹幕 TTS（C03）：队列上限 10、限速、跳过 URL/表情/超长文本、可一键静音。
/// 只负责「该不该读 / 读什么」，实际发声由 <c>ChatTtsService</c> 通过 SAPI 完成。
/// </summary>
public sealed class ChatTtsCore
{
    private readonly int _capacity;
    private readonly TimeSpan _minGap;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Queue<(string Text, DateTimeOffset At)> _queue = new();
    private DateTimeOffset _lastSpoken = DateTimeOffset.MinValue;
    public bool Muted { get; set; }

    public ChatTtsCore(int capacity = 10, TimeSpan? minGap = null, Func<DateTimeOffset>? clock = null)
    {
        _capacity = capacity;
        _minGap = minGap ?? TimeSpan.FromSeconds(2);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public int Pending => _queue.Count;

    public bool TryEnqueue(ChatMessage message, out string text)
    {
        text = "";
        if (Muted || message.Kind is "enter") return false;
        var cleaned = ChatCore.StripLinksAndEmoji(message.Text);
        if (cleaned.Length < 2 || cleaned.Length > 120) return false;
        if (cleaned.All(ch => !char.IsLetterOrDigit(ch))) return false;
        var now = _clock();
        if (now - _lastSpoken < _minGap) return false;
        if (_queue.Count >= _capacity) _queue.Dequeue();
        _queue.Enqueue((cleaned, now));
        _lastSpoken = now;
        text = cleaned;
        return true;
    }

    public bool TryDequeue(out string text)
    {
        text = "";
        if (_queue.Count == 0) return false;
        text = _queue.Dequeue().Text;
        return true;
    }

    public void Clear() => _queue.Clear();
}

/// <summary>
/// 弹幕互动（C04）：点歌 / 投票 / 抽奖 / 问答。全部是本机状态机，
/// 不自动向平台发消息；冷却与防刷在命令解析处统一处理。
/// </summary>
public static class ChatInteractionCore
{
    public sealed class Cooldown
    {
        private readonly Dictionary<string, long> _last = new(StringComparer.Ordinal);
        private readonly TimeSpan _gap;
        private readonly Func<DateTimeOffset> _clock;

        public Cooldown(TimeSpan gap, Func<DateTimeOffset>? clock = null)
        {
            _gap = gap;
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        public bool Allow(string key)
        {
            var now = _clock().UtcTicks;
            if (_last.TryGetValue(key, out var at) && now - at < _gap.Ticks) return false;
            _last[key] = now;
            return true;
        }
    }

    public sealed class SongQueue
    {
        private readonly Queue<(string User, string Title)> _queue = new();
        public int Count => _queue.Count;
        public IReadOnlyList<(string User, string Title)> Items => _queue.ToList();
        public bool Add(string user, string title)
        {
            if (string.IsNullOrWhiteSpace(title) || title.Length > 100) return false;
            _queue.Enqueue((user, title.Trim()));
            return true;
        }
        public (string User, string Title)? Next() => _queue.Count > 0 ? _queue.Dequeue() : null;
        public void Clear() => _queue.Clear();
    }

    public sealed class Poll
    {
        public string Question { get; }
        public IReadOnlyList<string> Options { get; }
        public bool Closed { get; private set; }
        private readonly Dictionary<int, HashSet<string>> _votes = new();

        public Poll(string question, IEnumerable<string> options)
        {
            Question = question;
            Options = options.Take(4).ToArray();
            for (var i = 0; i < Options.Count; i++) _votes[i] = new HashSet<string>(StringComparer.Ordinal);
        }

        public bool Vote(string userId, int option)
        {
            if (Closed || option < 0 || option >= Options.Count || string.IsNullOrEmpty(userId)) return false;
            foreach (var set in _votes.Values) set.Remove(userId);
            _votes[option].Add(userId);
            return true;
        }

        public IReadOnlyList<(string Option, int Votes)> Results()
            => Options.Select((o, i) => (o, _votes[i].Count)).ToList();

        public void Close() => Closed = true;
    }

    public sealed class Raffle
    {
        private readonly HashSet<string> _entries = new(StringComparer.Ordinal);
        public int Count => _entries.Count;
        public bool Enter(string userId) => !string.IsNullOrEmpty(userId) && _entries.Add(userId);
        public string? Draw(int seed)
        {
            if (_entries.Count == 0) return null;
            var list = _entries.OrderBy(x => x, StringComparer.Ordinal).ToList();
            return list[Math.Abs(seed) % list.Count];
        }
        public void Clear() => _entries.Clear();
    }

    /// <summary>解析 <c>!歌名 标题</c> / <c>!投票 1</c> 一类本地命令；不认识的返回 null。</summary>
    public static (string Command, string Argument)? ParseCommand(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text[0] is not ('!' or '！')) return null;
        var body = text[1..].Trim();
        if (body.Length == 0) return null;
        var space = body.IndexOf(' ');
        return space < 0 ? (body, "") : (body[..space], body[(space + 1)..].Trim());
    }
}

/// <summary>礼物 / 关注 / 进房特效（C05）：只允许白名单动作，带冷却，可整体关闭。</summary>
public static class ChatEffectCore
{
    public sealed record Rule(string Event, string Action, string Target, int CooldownSeconds);

    public static readonly Rule[] DefaultRules =
    {
        new("gift", "scene", "礼物特效", 5),
        new("follow", "sound", "alert", 10),
        new("enter", "none", "", 0),
    };

    public static Rule? Match(IEnumerable<Rule> rules, ChatMessage message)
    {
        var key = message.Kind switch
        {
            "gift" => "gift",
            "superchat" => "gift",
            "enter" => "enter",
            _ => "",
        };
        return key.Length == 0 ? null : rules.FirstOrDefault(r => r.Event == key && r.Action != "none");
    }
}

/// <summary>观众投稿 / 来信队列（C06）：上传 → 人工审核 → 上屏；类型/大小/路径校验在服务层。</summary>
public sealed class SubmissionQueue
{
    public sealed class Item
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string User { get; init; } = "";
        public string Text { get; init; } = "";
        public string? FilePath { get; init; }
        public string State { get; set; } = "pending"; // pending / approved / rejected
        public DateTimeOffset At { get; init; } = DateTimeOffset.Now;
    }

    private readonly List<Item> _items = new();
    private readonly object _gate = new();

    public IReadOnlyList<Item> Items { get { lock (_gate) return _items.ToList(); } }

    public Item Add(string user, string text, string? filePath = null)
    {
        var item = new Item { User = user ?? "", Text = (text ?? "").Trim(), FilePath = filePath };
        lock (_gate) _items.Add(item);
        return item;
    }

    public bool SetState(string id, string state)
    {
        if (state is not ("pending" or "approved" or "rejected")) return false;
        lock (_gate)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item is null) return false;
            item.State = state;
            return true;
        }
    }

    public void Clear() { lock (_gate) _items.Clear(); }
}

/// <summary>直播日历 / 预告 / 倒计时（C10）：本地日程、时区换算、下次开播提醒。</summary>
public static class CalendarCore
{
    public sealed class Entry
    {
        public string Title { get; init; } = "";
        public DateTimeOffset Start { get; init; }
        public int DurationMinutes { get; init; } = 120;
        public string TimeZoneId { get; init; } = TimeZoneInfo.Local.Id;
        public bool Reminder { get; init; } = true;
    }

    public static DateTimeOffset ToLocal(Entry entry, TimeZoneInfo? target = null)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(entry.TimeZoneId);
            var inZone = TimeZoneInfo.ConvertTime(entry.Start, zone);
            return TimeZoneInfo.ConvertTime(inZone, target ?? TimeZoneInfo.Local);
        }
        catch (TimeZoneNotFoundException) { return entry.Start; }
        catch (InvalidTimeZoneException) { return entry.Start; }
    }

    public static Entry? Next(IEnumerable<Entry> entries, DateTimeOffset now)
        => entries.Where(e => e.Start > now).OrderBy(e => e.Start).FirstOrDefault();

    public static TimeSpan Remaining(Entry? entry, DateTimeOffset now)
        => entry is null ? TimeSpan.Zero : entry.Start > now ? entry.Start - now : TimeSpan.Zero;

    /// <summary>倒计时文本 HH:MM:SS（跨天会超过 24 小时，按小时累加）。</summary>
    public static string Countdown(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        return $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }
}

/// <summary>
/// 翻译（C08 / S02）：默认本地短语表兜底，可注入云端引擎；限频、失败回退原文。
/// 云端必须由用户显式配置后才可能被调用（这里不发起任何网络请求）。
/// </summary>
public sealed class TranslationCore
{
    private readonly Func<string, string, Task<string?>>? _engine;
    private readonly LanPolicyRate _rate;

    public TranslationCore(Func<string, string, Task<string?>>? engine = null, int perMinute = 60)
    {
        _engine = engine;
        _rate = new LanPolicyRate(perMinute);
    }

    /// <summary>返回 (原文, 译文)；译文失败时等于原文并带错误码。</summary>
    public async Task<(string Source, string Translated, string Code)> TranslateAsync(string text, string target = "en")
    {
        if (string.IsNullOrWhiteSpace(text)) return ("", "", "");
        if (_engine is null)
        {
            var local = LocalGlossary.Translate(text, target);
            return local == text ? (text, text, FeatureErrors.TransEngineMissing) : (text, local, "");
        }
        if (!_rate.TryAcquire()) return (text, text, FeatureErrors.TransRateLimited);
        try
        {
            var result = await _engine(text, target).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(result)
                ? (text, text, FeatureErrors.TransEngineMissing)
                : (text, result!, "");
        }
        catch (Exception)
        {
            return (text, text, FeatureErrors.TransEngineMissing);
        }
    }

    /// <summary>双语字幕行：原文在上、译文在下；翻译失败时只输出原文。</summary>
    public static string BilingualLine(string source, string translated)
        => string.Equals(source, translated, StringComparison.Ordinal) || translated.Length == 0
            ? source
            : source + "\n" + translated;

    private sealed class LanPolicyRate
    {
        private readonly int _limit;
        private readonly Queue<long> _ticks = new();
        private readonly object _gate = new();
        public LanPolicyRate(int limit) => _limit = Math.Max(1, limit);
        public bool TryAcquire()
        {
            lock (_gate)
            {
                var now = DateTime.UtcNow.Ticks;
                while (_ticks.Count > 0 && _ticks.Peek() < now - TimeSpan.TicksPerMinute) _ticks.Dequeue();
                if (_ticks.Count >= _limit) return false;
                _ticks.Enqueue(now);
                return true;
            }
        }
    }
}

/// <summary>AI 弹幕回复建议（C07）：只给候选、不自动发；限频；必须人工确认。</summary>
public sealed class AiReplyCore
{
    private readonly TranslationCore? _translator;
    private readonly Func<ChatMessage, Task<IReadOnlyList<string>>>? _engine;
    private readonly Queue<long> _calls = new();

    public AiReplyCore(Func<ChatMessage, Task<IReadOnlyList<string>>>? engine = null, TranslationCore? translator = null)
    {
        _engine = engine;
        _translator = translator;
    }

    public async Task<(IReadOnlyList<string> Suggestions, string Code)> SuggestAsync(ChatMessage message)
    {
        var now = DateTime.UtcNow.Ticks;
        while (_calls.Count > 0 && _calls.Peek() < now - TimeSpan.TicksPerMinute) _calls.Dequeue();
        if (_calls.Count >= 6) return (Array.Empty<string>(), FeatureErrors.TransRateLimited);
        _calls.Enqueue(now);

        if (_engine is not null)
        {
            try
            {
                var list = await _engine(message).ConfigureAwait(false);
                if (list.Count > 0) return (list.Take(3).ToList(), "");
            }
            catch (Exception) { }
        }
        // 本地兜底：只给「安全、不承诺事实」的短句，明确标注需要人工确认
        var name = message.UserName.Length > 12 ? message.UserName[..12] : message.UserName;
        var suggestions = new[]
        {
            $"谢谢 {name} 的支持！",
            $"{name} 说得对，我记下了。",
            $"欢迎 {name}，稍后聊这个话题。",
        };
        return (suggestions, "");
    }

}

/// <summary>
/// 可选本地弹幕留存（C01 隐私边界）：默认关闭，最多 7 天；只存纯文本，不存 cookie / 登录态。
/// 目录可注入，可清空；日志文件名按日期，过期在启动与写入时清理。
/// </summary>
public sealed class ChatHistoryStore
{
    private readonly string _directory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    public bool Enabled { get; set; }
    public int RetentionDays { get; }

    public ChatHistoryStore(string directory, bool enabled = false, int retentionDays = 7, Func<DateTimeOffset>? clock = null)
    {
        _directory = directory;
        Enabled = enabled;
        RetentionDays = Math.Clamp(retentionDays, 1, 7);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        Prune();
    }

    public void Append(ChatMessage message)
    {
        if (!Enabled) return;
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_directory);
                var file = Path.Combine(_directory, _clock().ToLocalTime().ToString("yyyyMMdd") + ".jsonl");
                var line = JsonSerializer.Serialize(new
                {
                    at = message.At,
                    platform = message.Platform.ToString(),
                    room = message.Room,
                    user = message.UserName,
                    text = message.Text,
                    kind = message.Kind,
                });
                File.AppendAllText(file, line + "\n");
            }
        }
        catch (Exception) { }
    }

    public IReadOnlyList<string> LoadDay(DateOnly day)
    {
        var file = Path.Combine(_directory, day.ToString("yyyyMMdd") + ".jsonl");
        try { return File.Exists(file) ? File.ReadAllLines(file) : Array.Empty<string>(); }
        catch (Exception) { return Array.Empty<string>(); }
    }

    public void Prune()
    {
        try
        {
            if (!Directory.Exists(_directory)) return;
            var cutoff = _clock().ToLocalTime().Date.AddDays(-RetentionDays);
            foreach (var file in Directory.GetFiles(_directory, "*.jsonl"))
            {
                if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
            }
        }
        catch (Exception) { }
    }

    public void Clear()
    {
        try
        {
            if (!Directory.Exists(_directory)) return;
            foreach (var file in Directory.GetFiles(_directory, "*.jsonl")) File.Delete(file);
        }
        catch (Exception) { }
    }

    public int CountFiles()
    {
        try { return Directory.Exists(_directory) ? Directory.GetFiles(_directory, "*.jsonl").Length : 0; }
        catch (Exception) { return 0; }
    }
}

/// <summary>离线短语表（S02 兜底）：只覆盖直播常见短语；未命中时如实返回引擎缺失，不假装翻译成功。</summary>
public static class LocalGlossary
{
    private static readonly Dictionary<string, string> ZhToEn = new(StringComparer.OrdinalIgnoreCase)
    {
        ["谢谢"] = "thank you", ["你好"] = "hello", ["大家好"] = "hello everyone",
        ["欢迎"] = "welcome", ["下播"] = "ending the stream", ["开播"] = "going live",
        ["关注"] = "follow", ["点赞"] = "like", ["礼物"] = "gift", ["弹幕"] = "chat",
        ["游戏"] = "game", ["麦克风"] = "microphone", ["摄像头"] = "camera",
        ["今天"] = "today", ["明天"] = "tomorrow", ["好听"] = "sounds great",
        ["加油"] = "keep it up", ["再见"] = "goodbye", ["问题"] = "question",
        ["抽奖"] = "raffle", ["投票"] = "poll", ["点歌"] = "song request",
    };

    private static readonly Dictionary<string, string> EnToZh = new(StringComparer.OrdinalIgnoreCase)
    {
        ["thank you"] = "谢谢", ["hello"] = "你好", ["welcome"] = "欢迎",
        ["follow"] = "关注", ["like"] = "点赞", ["gift"] = "礼物",
        ["chat"] = "弹幕", ["game"] = "游戏", ["microphone"] = "麦克风",
        ["camera"] = "摄像头", ["today"] = "今天", ["tomorrow"] = "明天",
        ["goodbye"] = "再见", ["question"] = "问题", ["raffle"] = "抽奖",
        ["poll"] = "投票", ["song request"] = "点歌",
    };

    public static string Translate(string text, string target)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var table = target.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? EnToZh : ZhToEn;
        var result = text;
        foreach (var (key, value) in table.OrderByDescending(p => p.Key.Length))
            result = result.Replace(key, value, StringComparison.OrdinalIgnoreCase);
        return result;
    }
}
