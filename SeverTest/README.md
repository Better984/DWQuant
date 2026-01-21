# ASP.NET Core 8.0 服务器框架

## 项目概述

这是一个基于 ASP.NET Core 8.0 的控制台应用程序服务器框架，使用 Kestrel 作为 Web 服务器。

## 技术栈

- **框架**: ASP.NET Core 8.0
- **.NET 版本**: .NET 8.0
- **Web服务器**: Kestrel
- **项目类型**: 控制台应用程序（Console Application）

## 主要依赖包

- `Microsoft.AspNetCore.App` - ASP.NET Core 框架
- `Microsoft.Extensions.Hosting` (8.0.0) - 主机服务
- `Microsoft.Extensions.DependencyInjection` (8.0.0) - 依赖注入
- `MySqlConnector` (2.3.5) - MySQL 数据库连接器
- `Newtonsoft.Json` (13.0.4) - JSON 序列化/反序列化
- `System.IdentityModel.Tokens.Jwt` (8.0.2) - JWT 令牌处理
- `Microsoft.IdentityModel.Tokens` (8.0.2) - JWT 令牌验证
- `Google.Protobuf` (3.25.1) - Protocol Buffers 支持

## 项目结构

```
ServerTest/
├── Controllers/          # API 控制器
│   ├── BaseController.cs      # 基础控制器
│   └── HealthController.cs    # 健康检查控制器
├── Services/             # 业务服务层
│   ├── BaseService.cs         # 基础服务类
│   ├── DatabaseService.cs     # 数据库服务
│   └── JwtService.cs          # JWT 认证服务
├── Models/               # 数据模型
│   ├── BaseModel.cs          # 基础模型
│   └── ApiResponse.cs        # API 响应模型
├── Middleware/           # 中间件
│   └── ExceptionHandlingMiddleware.cs  # 异常处理中间件
├── Program.cs            # 程序入口
├── appsettings.json      # 应用配置
├── appsettings.Development.json  # 开发环境配置
└── ServerTest.csproj     # 项目文件
```

## 快速开始

### 1. 安装 .NET 8.0 SDK

确保已安装 .NET 8.0 SDK。

### 2. 配置数据库连接

编辑 `appsettings.json` 文件，配置数据库连接字符串：

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=testdb;User=root;Password=yourpassword;"
  }
}
```

### 3. 配置 JWT 设置

在 `appsettings.json` 中配置 JWT 相关设置：

```json
{
  "JwtSettings": {
    "SecretKey": "your-secret-key-here-minimum-32-characters",
    "Issuer": "ServerTest",
    "Audience": "ServerTest",
    "ExpirationMinutes": 60
  }
}
```

### 4. 运行项目

```bash
dotnet restore
dotnet build
dotnet run
```

服务器将在以下地址启动：
- HTTP: http://localhost:5000
- HTTPS: https://localhost:5001

### 5. 测试健康检查

访问 `http://localhost:5000/api/health` 检查服务器状态。

## 功能特性

### 1. 控制器基类
- `BaseController`: 提供统一的控制器基类，包含日志记录功能

### 2. 服务层
- `DatabaseService`: MySQL 数据库连接和操作服务
- `JwtService`: JWT 令牌生成和验证服务

### 3. 异常处理
- 全局异常处理中间件，统一处理未捕获的异常

### 4. API 响应格式
- 统一的 API 响应模型 `ApiResponse<T>`

### 5. CORS 支持
- 默认配置允许所有来源的跨域请求（开发环境）

### 6. Swagger 支持
- 开发环境自动启用 Swagger UI
- 访问 `http://localhost:5000/swagger` 查看 API 文档

## 开发指南

### 添加新的控制器

1. 在 `Controllers` 目录下创建新的控制器类
2. 继承 `BaseController` 基类
3. 使用 `[ApiController]` 和 `[Route]` 特性

示例：

```csharp
[ApiController]
[Route("api/[controller]")]
public class MyController : BaseController
{
    public MyController(ILogger<MyController> logger) : base(logger)
    {
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok("Hello World");
    }
}
```

### 添加新的服务

1. 在 `Services` 目录下创建新的服务类
2. 继承 `BaseService` 基类
3. 在 `Program.cs` 中注册服务

示例：

```csharp
builder.Services.AddScoped<MyService>();
```

### 使用数据库服务

```csharp
public class MyController : BaseController
{
    private readonly DatabaseService _dbService;

    public MyController(ILogger<MyController> logger, DatabaseService dbService) 
        : base(logger)
    {
        _dbService = dbService;
    }

    [HttpGet]
    public async Task<IActionResult> GetData()
    {
        using var connection = await _dbService.GetConnectionAsync();
        // 执行数据库操作
        return Ok();
    }
}
```

### 使用 JWT 服务

```csharp
public class AuthController : BaseController
{
    private readonly JwtService _jwtService;

    public AuthController(ILogger<AuthController> logger, JwtService jwtService) 
        : base(logger)
    {
        _jwtService = jwtService;
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        // 验证用户凭据
        var token = _jwtService.GenerateToken("userId", "username", new[] { "User" });
        return Ok(new { token });
    }
}
```

## 配置说明

### Kestrel 配置

在 `appsettings.json` 中配置 Kestrel 端点：

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:5000"
      },
      "Https": {
        "Url": "https://localhost:5001"
      }
    }
  }
}
```

## 注意事项

1. **安全性**: 生产环境中请修改默认的 CORS 策略，限制允许的来源
2. **JWT 密钥**: 生产环境必须使用强密钥，建议至少 32 个字符
3. **数据库连接**: 确保数据库连接字符串的安全性，不要将敏感信息提交到版本控制
4. **HTTPS**: 生产环境建议强制使用 HTTPS

## 许可证

本项目仅供学习和开发使用。
