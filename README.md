# QuickRun

Windows 小工具：把 exe / 快捷方式 / 文件夹拖进窗口，自动在快捷方式目录（默认 **`D:\Quick`**）生成
`.lnk` 快捷方式，并自动维护当前用户 PATH。无需管理员权限；存放目录以后可以随时更改。

- 详细说明：[使用说明.md](使用说明.md)
- 直接运行：`QuickRun.exe`
- 界面预览：`screenshots/main.png`
- 更改存放目录：`更多 → 更改快捷方式目录…`

```text
1. 双击 QuickRun.exe
2. 拖入 exe / 快捷方式
3. Win + R → 输入别名 → 回车
```

> 请复制到普通可写目录，在 Windows 资源管理器里双击运行。
> 从沙箱/对话界面/压缩包预览直接打开会禁止写快捷方式目录和用户 PATH，顶部会提示环境受限。
>
> 数据保存在 `%USERPROFILE%\QuickRun\data`；快捷方式目录里可能还有你手动放的快捷方式，
> 卸载时不要直接删除整个目录。

源码构建：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

---

作者：zhlipe
