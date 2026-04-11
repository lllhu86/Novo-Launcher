# Novo Launcher - 账号系统配置说明

## 登录系统概述

Novo Launcher 实现了类似 PCL2 的登录系统，支持以下两种登录方式：

1. **微软 OAuth 登录**（推荐）- 正版验证，安全
2. **离线登录** - 无需网络，适合没有购买游戏的玩家

## 微软 OAuth 登录配置

### 📢 重要提示

要让**所有用户**都能使用微软登录功能，你需要：

1. **申请公开的 Client ID** - 参考 [`APPLY_CLIENT_ID_GUIDE.md`](APPLY_CLIENT_ID_GUIDE.md)
2. **通过微软审核** - 通常需要 3-5 个工作日
3. **配置到启动器** - 将审核通过的 Client ID 填入代码

### 临时方案：快速测试

如果你只是想**快速测试**登录功能，可以按照以下步骤（但不适合公开发布）：

1. 访问 [Azure 门户](https://portal.azure.com/)
2. 导航到 **Azure Active Directory** > **应用注册**
3. 点击 **新注册**
4. 填写应用信息：
   - 名称：Novo Launcher
   - 支持的帐户类型：**任何组织目录中的帐户和个人 Microsoft 帐户**
   - 重定向 URI：暂时留空
5. 注册完成后，复制 **应用程序 (客户端) ID**

⚠️ **注意**: 使用这种方式获取的 Client ID **未经过微软审核**，可能：
- 只能在自己的账号上测试
- 无法在公开发布的版本中使用
- 有被微软封禁的风险

### 配置 Client ID

打开 `Services/AccountService.cs` 文件，找到第 24 行：

```csharp
private const string CLIENT_ID = "YOUR_CLIENT_ID"; // 需要替换为实际的 Client ID
```
```

将 `YOUR_CLIENT_ID` 替换为你申请的 Client ID。

### 注意事项

- Client ID 是敏感信息，不要公开分享
- 如果启动器要开源，请使用环境变量或配置文件
- 微软账号必须购买了 Minecraft 才能登录成功

## 账号存储

账号信息保存在以下路径：

```
%APPDATA%\NovoLauncher\accounts.json
```

### 账号数据格式

```json
{
  "Accounts": [
    {
      "Type": "offline",
      "Name": "PlayerName",
      "Uuid": "0123456789abcdef0123456789abcdef",
      "AccessToken": "",
      "RefreshToken": "",
      "PlayerType": "Legacy",
      "Auth": ""
    },
    {
      "Type": "microsoft",
      "Name": "PlayerName",
      "Uuid": "abcdef0123456789abcdef0123456789",
      "AccessToken": "eyJ0eXAiOiJKV1QiLCJhbGc...",
      "RefreshToken": "",
      "PlayerType": "msa",
      "Auth": ""
    }
  ],
  "SelectedIndex": 0
}
```

## 离线登录规则

离线登录时，玩家名称必须满足：

- 长度：3-16 个字符
- 只能包含字母（A-Z, a-z）和数字（0-9）
- 不能包含中文或其他特殊字符
- UUID 可选，留空则自动生成

## 游戏启动参数

启动游戏时，启动器会根据账号类型设置不同的参数：

### 离线账号
- `${auth_player_name}`: 玩家名称
- `${auth_uuid}`: UUID
- `${auth_access_token}`: UUID（离线模式使用 UUID 作为 token）
- `${user_type}`: Legacy

### 微软账号
- `${auth_player_name}`: 玩家名称
- `${auth_uuid}`: 微软返回的 UUID
- `${auth_access_token}`: Minecraft 访问令牌
- `${user_type}`: msa

## 使用指南

### 添加账号

1. 点击启动器顶部导航栏的 **账号** 标签
2. 选择登录方式：
   - **离线登录**：输入玩家名称（和可选的 UUID）
   - **微软登录**：自动打开浏览器，按照提示完成登录
3. 点击账号列表中的账号即可选中

### 切换账号

在账号页面点击任意账号即可切换

### 删除账号

1. 在账号列表中选择要删除的账号
2. 点击 **删除账号** 按钮
3. 确认删除

## 故障排除

### 微软登录失败

1. 检查是否正确配置了 Client ID
2. 确保微软账号已购买 Minecraft
3. 检查网络连接
4. 尝试重新登录

### 离线登录名称无效

- 确保名称长度为 3-16 个字符
- 只使用字母和数字
- 不要使用中文

### 游戏启动失败

1. 检查是否选择了账号
2. 确认账号信息正确
3. 查看日志文件获取详细错误信息

## 安全提示

1. 不要分享你的 `accounts.json` 文件
2. 微软账号的 AccessToken 具有访问权限，请妥善保管
3. 定期清理不再使用的账号信息
