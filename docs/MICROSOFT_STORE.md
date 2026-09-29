# Microsoft Store 发布

本项目使用 single-project MSIX。商店发布不需要把 PFX 证书放进仓库：将 MSIX 上传到 Partner Center 后，Microsoft 会在认证通过时重新签名。

## 生成提交包

1. 打开 GitHub Actions 的 **Microsoft Store package** workflow。
2. 点击 **Run workflow**。
3. 下载 `AiTranslator-Store-x64`、`AiTranslator-Store-x86` 和 `AiTranslator-Store-ARM64` artifacts。
4. 在 Partner Center 的同一个产品提交中上传适合目标设备的 `.msix` 文件。

本地等价命令：

```powershell
.\installer\build-msix.ps1 -Configuration Release -Platform x64 -OutputDirectory artifacts/store-x64
.\installer\build-msix.ps1 -Configuration Release -Platform ARM64 -OutputDirectory artifacts/store-arm64
```

## Partner Center

1. 从 [storedeveloper.microsoft.com](https://storedeveloper.microsoft.com/) 创建开发者账号并完成身份/企业验证。
2. 在 Partner Center 新建 **MSIX or PWA app**，预留“翻译助手”名称。
3. 创建提交，上传 MSIX，填写商店描述、截图、分类和年龄分级。
4. 提交认证。通过后，商店会负责签名、托管和更新。

不要把自签名开发包或 `.cer` 当作商店发布包。`installer/install-msix.ps1` 只用于本机开发测试。
