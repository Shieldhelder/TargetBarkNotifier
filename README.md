# Target Bark Notifier

`Target Bark Notifier` (TBN) 是一个轻量、开源的消息推送插件：

- 监听游戏内聊天消息
- 按规则匹配关键词
- 触发后发送手机推送（支持 Bark / NotifyMe）
- 可选 TTS 播报（系统语音）

请注意，TBN是纯Vibe Coding的产物，请您在使用本插件前，做好所有可能的心理准备。
------------
## 依赖

TBN需要在手机上安装Bark (ios) 或NotifyMe (安卓)，来实现消息Push。
作者更推荐使用Bark，太好用了APN服务。
- 获取Bark:  [https://bark.day.app/](https://bark.day.app/) 
- 获取NotifyMe:  [https://notifyme.521933.xyz/](https://notifyme.521933.xyz/)

## 命令

- `/tbn`：打开主窗口（配置规则、查看记录）
- `/tbn on`：启用插件（开始监听消息并按规则触发）
- `/tbn off`：禁用插件（停止监听与推送）
- `/tbn test`：发送测试推送（并按设置触发 TTS）
- `/tbn status`：查看当前开关状态与规则数量

## 功能概览

- 规则格式：`匹配的消息` + `消息标题` + `消息内容`
- 匹配标靶：`[channel]Sender:Message`
- 匹配条件支持 `|` 分隔，任意关键词命中即触发
- 推送内容可自定义，支持占位符：`{channel}` 频道、`{sender}` 发送者、`{message}` 消息主体
- 推送方式支持：
  - `Bark`
  - `NotifyMe`
- 两种推送方式可自由切换，同时开启
- 通知记录支持查看成功/失败详情

## 推送配置

### 前缀设置

- 可设置统一的推送前缀（如 `[1号机]`）
- 前缀位置可选：不添加、仅标题、仅内容、标题和内容

### Bark

- 填写 `Bark Token`
- 推送接口格式：

```text
https://api.day.app/{token}/{title}/{content}
```

### NotifyMe

- 填写 `NotifyMe UUID`
- 推送接口格式：

```text
https://notifyme-server.wzn556.top/?uuid={uuid}&title={title}&body={content}
```
- 推荐使用极光推送
- 作者没有试过其他的推送方式，只能估测他们都可以正常使用。
- 由于OPPO Push的限制，国行设备每天只能发送两条消息，不推荐使用。
  具体请参考NotifyMe的相关文档。
[https://notifyme.521933.xyz/push_type/oppo_push.html](https://notifyme.521933.xyz/push_type/oppo_push.html)

## 规则管理

- 支持新增、删除、右键编辑规则
- 支持规则导入/导出（JSON），为整体覆盖
- 支持启用/禁用单条规则

## 通知记录

记录内容包括：

- 时间
- 匹配规则
- 推送方式
- Token/UUID
- 标题
- 内容
- 成功/失败详情

通知本身不会被TBN以任何形式储存，但由于TBN依赖于Bark/NotifyMe的推送服务，烦请阅读他们的隐私协议。

## TTS 说明

- `启用TTS播报` 后，命中规则会播报原始消息
- 使用系统语音通道，独立于游戏内音量设置

## 构建与加载

1. 按本机环境确认 `TargetBarkNotifier.csproj` 中 `DalamudLibPath`
2. 构建项目：

```bash
dotnet build TargetBarkNotifier.csproj
```

3. 将输出的 `dll/json` 放入 Dalamud 开发插件目录并加载
