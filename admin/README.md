# Admin 后台管理系统

基于 React + TypeScript + Vite + Ant Design 构建的管理后台系统。

## 技术栈

- **React 19** - UI 框架
- **TypeScript** - 类型安全
- **Vite** - 构建工具
- **Ant Design** - UI 组件库
- **React Router** - 路由管理
- **Axios** - HTTP 客户端

## 快速开始

### 安装依赖

```bash
npm install
```

### 启动开发服务器

```bash
npm run dev
```

应用将在 `http://localhost:3001` 启动。

### 构建生产版本

```bash
npm run build
```

## 组件库使用

项目已集成 **Ant Design** 组件库，所有组件统一从 `src/components/ui/index.ts` 导出。

### 基础用法

```tsx
import { Button, Input, Card, Table, message } from '@/components/ui';

function MyComponent() {
  const handleClick = () => {
    message.success('操作成功');
  };

  return (
    <Card>
      <Input placeholder="请输入" />
      <Button type="primary" onClick={handleClick}>
        提交
      </Button>
    </Card>
  );
}
```

### 可用组件

#### 基础组件
- `Button` - 按钮
- `Input` - 输入框
- `Input.Password` - 密码输入框
- `InputNumber` - 数字输入框
- `TextArea` - 文本域
- `Select` - 选择器
- `DatePicker` - 日期选择器
- `TimePicker` - 时间选择器
- `Switch` - 开关
- `Checkbox` - 复选框
- `Radio` - 单选框
- `Upload` - 上传
- `Rate` - 评分
- `Slider` - 滑动输入条

#### 布局组件
- `Layout` - 布局容器
- `Row` / `Col` - 栅格布局
- `Space` - 间距
- `Divider` - 分割线
- `Grid` - 网格布局

#### 数据展示
- `Table` - 表格
- `Card` - 卡片
- `Tag` - 标签
- `Badge` - 徽标数
- `Empty` - 空状态
- `Statistic` - 统计数值
- `Timeline` - 时间轴
- `Tree` - 树形控件
- `Progress` - 进度条
- `Avatar` - 头像

#### 反馈组件
- `Modal` - 对话框
- `Drawer` - 抽屉
- `message` - 全局提示
- `notification` - 通知提醒框
- `Alert` - 警告提示
- `Popconfirm` - 气泡确认框
- `Spin` - 加载中
- `Progress` - 进度条

#### 导航组件
- `Menu` - 导航菜单
- `Tabs` - 标签页
- `Breadcrumb` - 面包屑
- `Pagination` - 分页
- `Steps` - 步骤条
- `Anchor` - 锚点
- `BackTop` - 回到顶部

#### 其他组件
- `Tooltip` - 文字提示
- `Popover` - 气泡卡片
- `Dropdown` - 下拉菜单
- `Affix` - 固钉
- `PageHeader` - 页头
- `AutoComplete` - 自动完成
- `Mentions` - 提及
- `Cascader` - 级联选择
- `Transfer` - 穿梭框
- `TreeSelect` - 树选择
- `Calendar` - 日历

### Icons 图标

项目已集成 `@ant-design/icons`，所有图标统一导出：

```tsx
import { 
  UserOutlined, 
  SettingOutlined, 
  LogoutOutlined 
} from '@/components/ui';

function MyComponent() {
  return (
    <>
      <UserOutlined />
      <SettingOutlined />
      <LogoutOutlined />
    </>
  );
}
```

### 主题配置

主题配置在 `src/App.tsx` 中的 `ConfigProvider`：

```tsx
<ConfigProvider
  locale={zhCN}
  theme={{
    token: {
      colorPrimary: '#1890ff',
      borderRadius: 6,
    },
  }}
>
  {/* ... */}
</ConfigProvider>
```

### 消息提示

#### message（全局提示）

```tsx
import { message } from '@/components/ui';

// 成功提示
message.success('操作成功');

// 错误提示
message.error('操作失败');

// 警告提示
message.warning('请注意');

// 信息提示
message.info('这是一条信息');
```

#### notification（通知提醒框）

```tsx
import { notification } from '@/components/ui';

notification.open({
  message: '通知标题',
  description: '通知内容描述',
  placement: 'topRight',
});
```

### 表单处理

使用 Ant Design 的 Form 组件：

```tsx
import { Form, Input, Button } from '@/components/ui';

function LoginForm() {
  const [form] = Form.useForm();

  const onFinish = (values: any) => {
    console.log('表单数据:', values);
  };

  return (
    <Form form={form} onFinish={onFinish}>
      <Form.Item
        name="email"
        rules={[{ required: true, message: '请输入邮箱' }]}
      >
        <Input placeholder="邮箱" />
      </Form.Item>
      <Form.Item>
        <Button type="primary" htmlType="submit">
          提交
        </Button>
      </Form.Item>
    </Form>
  );
}
```

### 表格使用

```tsx
import { Table } from '@/components/ui';

const columns = [
  {
    title: '姓名',
    dataIndex: 'name',
    key: 'name',
  },
  {
    title: '邮箱',
    dataIndex: 'email',
    key: 'email',
  },
];

const data = [
  { key: '1', name: '张三', email: 'zhangsan@example.com' },
  { key: '2', name: '李四', email: 'lisi@example.com' },
];

function MyTable() {
  return <Table columns={columns} dataSource={data} />;
}
```

## 项目结构

```
admin/
├── src/
│   ├── components/        # 组件
│   │   ├── ui/           # UI 组件（统一导出 Ant Design）
│   │   └── ...          # 其他业务组件
│   ├── pages/           # 页面
│   ├── network/         # 网络请求
│   ├── auth/            # 认证相关
│   ├── App.tsx          # 根组件
│   └── main.tsx         # 入口文件
├── public/              # 静态资源
└── package.json         # 依赖配置
```

## 开发规范

1. **组件导入**：统一从 `@/components/ui` 导入 Ant Design 组件
2. **类型定义**：使用 TypeScript 严格类型检查
3. **代码风格**：遵循 ESLint 规则
4. **组件命名**：使用 PascalCase
5. **文件命名**：使用 PascalCase（组件）或 camelCase（工具函数）

## 更多文档

- [Ant Design 官方文档](https://ant.design/docs/react/introduce-cn)
- [React Router 文档](https://reactrouter.com/)
- [Vite 文档](https://cn.vitejs.dev/)
