using Dalamud.Bindings.ImGui;

namespace TargetBarkNotifier;

public sealed partial class MainWindow
{
    private static void DrawMatchHelp()
    {
        var ver = typeof(MainWindow).Assembly.GetName().Version;
        var version = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}" : "0.0.0.1";
        ImGui.Text($"版本: {version}");
        ImGui.Separator();
        ImGui.Text("TBN会按照如下步骤进行识别与Push消息");
        ImGui.Text("1. 标靶构造");
        ImGui.BulletText("TBN收到任意消息后，会将其拆分为 {channel} 频道、{sender} 发送者、{message} 消息主体");
        ImGui.BulletText("然后按照固定格式构造一个标靶 [channel]Sender:Message");
        ImGui.BulletText("请注意：标靶本身不支持修改");
        ImGui.BulletText("例如: [tellincoming][游戏管理员]丝瓜卡夫卡：别开挂了");

        ImGui.Spacing();
        ImGui.Text("2. 匹配规则");
        ImGui.BulletText("匹配关键词支持 | (OR) 与 + (AND)");
        ImGui.BulletText("例如设定关键词为: 游戏管理员");

        ImGui.Spacing();
        ImGui.Text("2.1 匹配语法示例");
        ImGui.BulletText("单关键词: 管理员私聊（命中该词即可）");
        ImGui.BulletText("OR: 管理员私聊|队长点名（任一命中即可）");
        ImGui.BulletText("AND: 123+456（同一条消息需同时包含123与456）");
        ImGui.BulletText("混合: 123+456|队长点名（先按|拆分，再对每段按+做同时匹配）");

        ImGui.Spacing();
        ImGui.Text("3. 推送内容");
        ImGui.BulletText("推送标题及内容均可自定义，支持占位符 {channel}/{sender}/{message}/{name}/{server}/{currentserver}");
        ImGui.BulletText("其中 {name} 为角色名，{server} 为归属服务器（HomeWorld），{currentserver} 为当前所在服务器（CurrentWorld）");
        ImGui.BulletText("可用于标题、内容和推送前缀，例如设定内容为: [{name}@{server}] 检测到可能的GM私聊：{sender}:{message}");

        ImGui.Spacing();
        ImGui.Text("4. 触发效果");
        ImGui.BulletText("当收到 [游戏管理员]丝瓜卡夫卡 发来的 别开挂了 的私聊，会构造 [tellincoming][游戏管理员]丝瓜卡夫卡：别开挂了 的标靶");
        ImGui.BulletText("命中关键词 游戏管理员，触发推送。 内容为: 检测到可能的GM私聊：[游戏管理员]丝瓜卡夫卡:别开挂了");

        ImGui.Spacing();
        ImGui.Text("5. 推送服务");
        ImGui.BulletText("支持 Bark (iOS)、NotifyMe (Android) 和 Server酱3 (微信/多渠道)");
        ImGui.BulletText("Server酱3 支持标准 SendKey (SCTxxx) 和企业微信应用 (sctp 开头)");
        ImGui.BulletText("三种推送方式可同时开启，互不影响");

        ImGui.Spacing();
        ImGui.Text("6. 推送重试机制");
        ImGui.BulletText("推送失败时自动重试，最多重试 5 次，每次间隔 30 秒");
        ImGui.BulletText("仅当服务端错误(HTTP 5xx)或请求超时时触发重试");
        ImGui.BulletText("客户端错误(HTTP 4xx)或网络错误不会重试");
        ImGui.BulletText("重试时消息内容会标记重试次数，如：内容（重连3次）");

        ImGui.Spacing();
        ImGui.Text("TBN不会留存您的消息，但由于TBN依赖于Bark/Notifyme/Server酱3的消息推送服务，请仔细阅读他们的隐私策略。");
    }
}
