# Target Bark Notifier

`Target Bark Notifier` (TBN) 是一个轻量、开源的消息推送插件：

- 监听游戏内聊天消息
- 按规则匹配关键词
- 触发后发送手机推送（支持 Bark / NotifyMe / Server酱3）
- 可选 TTS 播报（系统语音）

请注意，TBN是纯Vibe Coding的产物，请您在使用本插件前，做好所有可能的心理准备。
------------
## 依赖

TBN需要在手机上安装Bark (iOS)、NotifyMe (安卓)
也可以安装更方便的Server酱3，iOS/FCM/小米/华为/荣耀/vivo/oppo/iQOO/Realme/OnePlus/魅族手机支持无后台推送。
- 获取Bark:  [https://bark.day.app/](https://bark.day.app/)
- 获取NotifyMe:  [https://notifyme.521933.xyz/](https://notifyme.521933.xyz/)
- 获取Server酱3:  [https://sc3.ft07.com/client/](https://sc3.ft07.com/client)

## 功能概览

- 规则格式：`匹配的消息` + `消息标题` + `消息内容`
- 匹配标靶：`[channel]Sender:Message`
- 匹配条件支持 `|` 分隔，任意关键词命中即触发
- 推送内容可自定义，支持占位符：`{channel}` 频道、`{sender}` 发送者、`{message}` 消息主体、`{name}` 角色名、`{server}` 归属服务器、`{currentserver}` 当前服务器
- 推送方式支持：
  - `Bark` (iOS APN推送)
  - `NotifyMe` (Android多渠道推送)
  - `Server酱3` (多渠道推送)
- 三种推送方式可自由组合，同时开启
- 通知记录支持查看成功/失败详情

## 推送配置

### 前缀设置

- 可设置统一的推送前缀（如 `[1号机]`）
- 前缀位置可选：不添加、仅标题、仅内容、标题和内容

### Bark

- 填写 `Bark Token`
- 由于使用APN服务，无需额外配置。

### NotifyMe

- 填写 `NotifyMe UUID`
- 推荐使用极光推送渠道
- 作者没有试过其他的推送方式，只能估测他们都可以正常使用。
- 由于OPPO Push的限制，国行设备每天只能发送两条消息，不推荐使用。
  具体请参考NotifyMe的相关文档。
[https://notifyme.521933.xyz/push_type/oppo_push.html](https://notifyme.521933.xyz/push_type/oppo_push.html)

### Server酱3
- 获取并填写 `Server酱3 SendKey`
使用微信登录Oauth后，获取SendKey, 并粘贴至TBN中使用。
[https://sc3.ft07.com/sendkey](https://sc3.ft07.com/sendkey)

## 规则管理

- 支持新增、删除、右键编辑规则
- 支持规则导入/导出（JSON），为整体覆盖
- 支持启用/禁用单条规则

## 通知记录

记录内容包括：

- 时间
- 匹配规则
- 推送方式
- 标识符(Token/UUID/SendKey)
- 标题
- 内容
- 成功/失败详情

### 推送重试机制

- 推送失败时自动重试，最多重试 5 次，每次间隔 30 秒
- 仅当服务端错误(HTTP 5xx)或请求超时时触发重试
- 客户端错误(HTTP 4xx)或网络错误不会重试，立即返回失败
- 重试时消息内容会标记重试次数，如：`内容（重连3次）`

通知本身不会被TBN以任何形式储存，但由于TBN依赖于Bark/NotifyMe/Server酱3的推送服务，烦请阅读他们的隐私协议。

## TTS 说明

- `启用TTS播报` 后，命中规则会播报原始消息
- 使用系统语音通道，独立于游戏内音量设置

## 掉线监控与客户端心跳监控

- `启用掉线监控` 后，TBN会监控游戏内Addon的状态。如果发现掉线对话框，则触发推送。
- `启用在线监控` 后，TBN会与局域网内的TBN Monitor握手，每隔一定时长向TBN Monitor发送存活心跳。如果客户端因为各种原因卡死失联，均会由TBN Monitor发送Push请求。
- 获取TBN Monitor：请访问Github release. TBN Monitor的使用暂不会提供任何技术支持。
[https://github.com/Shieldhelder/TBNMonitor](https://github.com/Shieldhelder/TBNMonitor)

## 构建与加载

1. 按本机环境确认 `TargetBarkNotifier.csproj` 中 `DalamudLibPath`
2. 构建项目：

```bash
dotnet build TargetBarkNotifier.csproj
```

3. 将输出的 `dll/json` 放入 Dalamud 开发插件目录并加载
