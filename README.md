# WattWatch · 无线设备电量监控

Windows 托盘小工具：把 2.4G 接收器和蓝牙外设的电量集中显示，**默认 30 分钟才查询一次**，并且每次查询都自己开关句柄、失败指数退避——目标是"能看见电量，又不会把鼠标电池耗掉"。

> 本项目是在试用 [evren-oct/vigil-watt](https://github.com/evren-oct/vigil-watt) 之后另写的实现：
> 它没认出 1532:00C1 的毒蝰 V3 Pro，也不支持八爪鱼 5；而且主动查询频率偏高。
> 这里换了一套"被动优先 + 低频主动"的调度，并把已验证的协议整理成独立探针。

## 已支持设备

| 设备 | 识别方式 | 状态 |
| --- | --- | --- |
| 雷蛇 毒蝰 Viper V3 Pro | `1532:00C1`，90 字节 feature report（class 0x07 / 0x80） | 本机验证：17% 与 Synapse 一致 |
| VGN V98 Pro | `320F:5088`，0xFF1C 通道，report id 0x04 的 `0x1A` 查询 | 本机验证：77% |
| 漫步者 W830NB | 经典蓝牙 HFP 网关节点的 `DEVPKEY_Device_BatteryLevel` | 本机验证：20% |
| 飞智 八爪鱼 5 | `37D7:2501`，`5A A5 01` GET_INFO | 本机验证：100% |
| 索尼 DualSense / Edge | `054C:0CE6` / `0DF2`，读 feature 0x09 后解析 0x31 状态报文 | 本机验证：55% |
| 其它蓝牙设备 | Windows 已缓存的电量属性（含 BLE，如 Xbox 手柄） | 自动识别 |
主界面底部有「其他设备（N）」入口：系统里能看到、但目前没有电量读取方式的设备（例如雷蛇声卡 `1532:0529`、飞智 CD2 `37D7:6001`、主板 ITE `048D:5702`）会列在那里，并标注「未适配电量协议」，方便判断到底是"没插上"还是"插上了但没协议"。

## 为什么更省电

| 做法 | 效果 |
| --- | --- |
| 默认 30 分钟一次，可调到 5–60 分钟 | 相比十几秒一轮的轮询，链路唤醒次数下降一个数量级 |
| 每读一次就重新打开 → 关闭 HID 句柄 | 不让接收器/设备因为"有人一直挂在输入通道上"而保持唤醒 |
| 蓝牙电量直接读 Windows 已缓存的设备属性 | 不建 GATT 连接、不发 AT 命令，**对耳机零开销** |
| 设备无应答时指数退避（最长 4 小时） | 不再反复戳一台已经睡着的鼠标 |
| 只做 15 秒一次的设备树重扫（纯元数据，无设备 I/O） | 插拔发现及时，且不产生任何无线流量 |
| 没有 BLE 扫描、没有输入报文订阅 | 后台常驻时 CPU 与蓝牙活动接近 0 |

## 使用

1. 运行 `WattWatch.exe`（或 `WattWatch.exe --tray` 静默启动）。
2. 托盘图标显示当前最低电量，左键打开主界面，右键菜单可刷新 / 设置 / 退出。
3. 设置里可改查询间隔、低电量阈值、开机自启、关闭时是否缩到托盘。
4. 历史数据保存在 `%LOCALAPPDATA%\WattWatch\history\*.jsonl`，设置在同目录 `settings.json`。
   有了 12 小时以上的放电记录后，卡片上会多一行"预计还可用 x 小时"。

## 构建

需要 .NET 8 SDK（本机放在 `D:\tools\dotnet`，脚本会自动找）。

```powershell
# 调试构建
dotnet build WattWatch.sln -c Debug

# 发布单文件（自带运行时，约 150 MB）
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1

# 发布依赖框架版（约 2 MB，需已安装 .NET 8 Desktop Runtime）
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 -FrameworkDependent
```

## 结构

```
src/WattWatch.Core/      HID 枚举与句柄、协议探针、蓝牙属性读取、调度、历史
src/WattWatch.App/       WPF 托盘界面
tools/WattWatch.Probe/   命令行验证工具（list / read / dump）
```

命令行验证（不启动界面）：

```powershell
WattWatch.Probe.exe list          # 列出 HID 通道与蓝牙电量节点
WattWatch.Probe.exe read          # 对每台设备读一次并打印原始依据
WattWatch.Probe.exe dump 37D7:2501 5   # 被动抓 5 秒输入报文，用于适配新设备
```

## 已知限制

- 雷蛇/飞智协议要发一帧查询，因此**无法做到完全零写入**；本项目做的是把频率压到最低并明确退避。
- 八爪鱼 5 的协议来自 SDL 的 hidapi 驱动（`SDL_hidapi_flydigi.c`），尚未在本机真机验证；如读数不对，用 `WattWatch.Probe.exe dump 37D7:2501` 抓包后按帧调整 `FlydigiProtocol`。
- 蓝牙耳机的电量依赖耳机自己通过 HFP 上报；有些耳机只在开机/连接时上报一次。
- 同类设备重名时会自动加上 MAC 尾号区分（如两台 Xbox 手柄）。

## 致谢

协议细节参考了 [OpenRazer](https://github.com/openrazer/openrazer)（雷蛇 90 字节帧）、
[SDL](https://github.com/libsdl-org/SDL)（飞智 V2 帧）、VigilWatt 的硬件文档（VGN / 雷蛇 / 蓝牙节点思路）。
代码为独立实现。
