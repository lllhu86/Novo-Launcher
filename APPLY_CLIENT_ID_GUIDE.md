# 申请公开的 Microsoft OAuth Client ID 指南

## 为什么需要申请？

微软为了保护用户安全，要求所有第三方启动器必须申请并获得批准后才能使用微软登录功能。这是为了防止恶意应用窃取用户账号。

## 申请步骤

### 第一步：注册 Azure 账号

1. 访问 https://portal.azure.com/
2. 使用你的微软账号登录（如果没有，需要注册一个）
3. 创建免费订阅（如果需要）

### 第二步：注册应用

1. 在 Azure 门户中，搜索并选择 **"Azure Active Directory"** 或 **"Microsoft Entra ID"**
2. 在左侧菜单中选择 **"应用注册"** (App registrations)
3. 点击 **"新注册"** (New registration)
4. 填写应用信息：
   - **名称**: Novo Launcher（或你的启动器名称）
   - **支持的帐户类型**: 选择 **"任何组织目录中的帐户和个人 Microsoft 帐户"**
   - **重定向 URI**: 暂时留空（我们使用设备代码流程，不需要重定向 URI）
5. 点击 **"注册"**

### 第三步：配置应用

1. 注册完成后，在应用概览页面复制 **应用程序 (客户端) ID**
2. 在左侧菜单选择 **"身份验证"** (Authentication)
3. 启用以下选项：
   - ✅ **允许公共客户端流** (Allow public client flows)
   - ✅ **Live SDK 支持** (如果可用)
4. 点击 **"保存"**

### 第四步：申请 XboxLive.signin 权限

**重要**: 这一步是关键！没有这个权限，用户无法使用微软登录玩 Minecraft。

1. 访问申请表单：https://aka.ms/mce-reviewappid
2. 填写以下信息：
   - **应用名称**: Novo Launcher
   - **应用描述**: 一个开源的 Minecraft 启动器，为非商业用途开发
   - **公司/组织**: 个人开发者（如果是个人项目）
   - **联系邮箱**: 你的邮箱
   - **GitHub 仓库**: 如果有开源仓库，提供链接
   - **使用场景**: 用于用户通过微软账号登录 Minecraft

3. 在描述中强调：
   - 这是一个**非商业**项目
   - 用于**教育/学习**目的
   - 会**保护用户隐私和数据安全**
   - 遵循微软的**使用条款**

### 第五步：等待审核

- 审核时间通常为 **3-5 个工作日**
- 审核结果会通过邮件通知
- 如果未通过，根据反馈修改后重新提交

### 第六步：配置到启动器

审核通过后：

1. 打开 `AccountService.cs` 文件
2. 找到第 24 行
3. 将 Client ID 替换为你申请的 ID：
   ```csharp
   private const string CLIENT_ID = "你的-client-id-在这里";
   ```
4. 重新编译启动器

## 申请成功的关键点

### ✅ 应该做的：
- 明确说明是**非商业**项目
- 强调**教育/学习**目的
- 提供**GitHub 仓库**链接（如果开源）
- 说明会**保护用户数据安全**
- 提供真实的联系信息

### ❌ 不应该做的：
- 不要声称是商业项目（除非真的是）
- 不要提供虚假信息
- 不要抄袭其他启动器的描述
- 不要忽略隐私和安全说明

## 常见问题

### Q: 我是学生，只是想做毕设，能通过吗？
A: 可以！在教育目的下申请，说明是学校项目，通常能获批。

### Q: 我想开源这个启动器，需要注意什么？
A: 
- 不要将 Client ID 直接提交到公开仓库
- 使用配置文件或环境变量
- 在 README 中说明用户需要自行申请 Client ID

### Q: 申请被拒绝了怎么办？
A: 
- 仔细阅读拒绝原因
- 根据反馈修改申请
- 可以重新提交申请
- 或者联系微软支持

### Q: 多久需要重新申请？
A: 
- Client ID 通常**永久有效**
- 但如果违反使用条款，可能被撤销
- 定期检查应用状态

## 替代方案

如果申请失败或等待时间太长，你可以：

### 方案 A：让用户自行配置
- 提供详细的配置教程
- 用户可以自己去申请 Client ID
- 适合技术用户

### 方案 B：使用离线登录
- 离线登录不需要 Client ID
- 适合没有网络或不想登录的情况
- 但无法使用正版皮肤和联机

## 参考资源

- [Microsoft OAuth 2.0 设备代码流程](https://learn.microsoft.com/azure/active-directory/develop/v2-oauth2-device-code)
- [Xbox Live 认证文档](https://wiki.vg/Microsoft_Authentication)
- [Azure 应用注册指南](https://learn.microsoft.com/azure/active-directory/develop/quickstart-register-app)

## 联系支持

如果遇到问题：
- Microsoft Q&A: https://learn.microsoft.com/answers/
- GitHub Issues: （你的项目 Issues 页面）
- 邮箱：（你的联系邮箱）

---

**祝你申请顺利！🎉**
