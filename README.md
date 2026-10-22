# QuickRun

> ✨ Windows Win+R 别名管理工具｜拖拽文件一键创建快捷方式，自动维护用户 PATH，**无需管理员权限**

QuickRun 是一款轻量 Windows 效率工具：把 exe / 快捷方式 / 文件夹拖入窗口，自动在指定目录生成
`.lnk` 快捷方式，并自动把该目录加入**当前用户 PATH**。配置完成后，按下 `Win+R`，输入别名即可直接启动程序，减少桌面图标。

![QuickRun 界面预览](screenshots/main.png)

## ✅ 特性

- 🖱️ **拖拽式添加**：支持 exe、快捷方式、文件夹，可一次拖入多个
- 📌 **用户级 PATH**：只修改 `HKCU\Environment`，不动系统环境变量，不需要管理员权限
- 📄 **原生 `.lnk`**：由 Windows Shell 直接启动，不经过 `cmd.exe`，不闪黑框；拖入已有快捷方式时保留图标、启动参数和工作目录
- 📂 **自定义存放目录**：默认 `D:\Quick`，可随时在「更多 → 更改快捷方式目录…」更换并迁移现有别名
- 🔁 **自动维护**：自动生成/重建快捷方式，自动避让重名，支持一键修复环境
- 🪶 **单个 exe**：无需安装 .NET SDK；Windows 10/11 自带 .NET Framework 4.x 即可运行
- 📝 **简单易用**：支持编辑别名、打开目录、环境自检、从用户 PATH 移除

## 📥 下载

前往 [Releases](https://github.com/zhlipe/QuickRun/releases) 页面下载：

- `QuickRun.exe`：单文件，直接运行
- `QuickRun-1.0.0.zip`：完整包，包含程序、使用说明和界面截图

> 建议复制到普通可写目录，在 Windows 资源管理器里双击运行。
> 从沙箱、对话界面或压缩包预览直接打开会限制写入快捷方式目录和用户 PATH。

## 📖 使用教程

1. 双击 `QuickRun.exe`。
2. 保持默认快捷方式目录 `D:\Quick`，或点右上角 `⋯ 更多 → 更改快捷方式目录…` 选择自己的目录。
3. 把 exe / 快捷方式 / 文件夹拖进窗口，程序自动生成 `<别名>.lnk`。
4. 按 `Win+R`，输入别名回车即可启动，不用带 `.lnk` 后缀。

> 首次使用若 `Win+R` 找不到别名，点「修复环境」，或注销/重启资源管理器让 PATH 刷新。

## 🗂️ 文件与数据

- 快捷方式目录：默认 `D:\Quick`，可在程序内随时更改
- 数据目录：`%USERPROFILE%\QuickRun\data`
  - `config.json`：别名配置
  - `settings.json`：快捷方式目录设置
  - `quickrun.log`：运行日志
- 只修改当前用户 PATH，不写系统级 PATH

## 📌 和同类工具对比

| 方案 | 运行方式 | 特点 |
| --- | --- | --- |
| **QuickRun** | 原生 `Win+R` + `.lnk` | 无需常驻启动器；快捷方式由 Windows Shell 直接启动，不经过 `cmd.exe` |
| lnb（cmd 垫片方案） | cmd 脚本转发 | 兼容性好，但会经过命令行；QuickRun 使用系统原生 `.lnk`，更干净 |
| PowerToys Run | 独立启动器 | 功能更强，但需要额外启动器/快捷键；QuickRun 直接复用系统 `Win+R` |

## ❓ 常见问题

**Q: 需要管理员权限吗？**  
不需要。只写当前用户注册表、用户目录和快捷方式目录。

**Q: 启动时会闪黑窗口吗？**  
不会。别名是 `.lnk` 快捷方式，由 Windows Shell 直接启动目标，不经过 `cmd.exe`。

**Q: 目标程序移动或删除了？**  
列表状态会显示「目标丢失」，选中后点「编辑」，重新选择目标即可。

**Q: 快捷方式存放目录可以改吗？**  
可以。`更多 → 更改快捷方式目录…`，现有别名会自动迁移到新目录；旧目录里手动放置的文件不会被删除。

更多说明见 [使用说明.md](使用说明.md)。

## 🛠️ 从源码构建

需要 Windows 自带的 .NET Framework 4.x，不需要单独安装 .NET SDK：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

构建产物：`QuickRun.exe`

## 🔐 安全说明

- 不联网、不上传任何数据
- 不请求管理员权限
- 只写当前用户目录、快捷方式目录和 `HKCU\Environment` 的 PATH

---

作者：zhlipe
