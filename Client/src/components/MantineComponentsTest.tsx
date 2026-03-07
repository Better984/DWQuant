import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import '@mantine/core/styles.css';
import '@mantine/notifications/styles.css';
import {
  Accordion,
  ActionIcon,
  Alert,
  Avatar,
  Badge,
  Button,
  Card,
  Checkbox,
  Code,
  ColorInput,
  Divider,
  Drawer,
  Group,
  HoverCard,
  MantineProvider,
  Menu,
  Modal,
  MultiSelect,
  Notification as MantineNotification,
  NumberInput,
  Paper,
  PasswordInput,
  Popover,
  Progress,
  Radio,
  RangeSlider,
  RingProgress,
  SegmentedControl,
  Select,
  SimpleGrid,
  Slider,
  Stack,
  Stepper,
  Switch,
  Table,
  Tabs,
  TagsInput,
  Text,
  TextInput,
  Textarea,
  ThemeIcon,
  Timeline,
  Title,
  Tooltip,
  createTheme,
} from '@mantine/core';
import { Notifications, notifications } from '@mantine/notifications';
import { useDisclosure } from '@mantine/hooks';
import {
  IconArrowLeft,
  IconArrowUpRight,
  IconBellRinging,
  IconBolt,
  IconBrandReact,
  IconChecklist,
  IconCode,
  IconDatabase,
  IconMoonStars,
  IconPalette,
  IconPlayerPlay,
  IconSearch,
  IconSettings,
  IconShieldCheck,
  IconSunHigh,
  IconUsers,
} from '@tabler/icons-react';
import './MantineComponentsTest.css';

const mantineTheme = createTheme({
  primaryColor: 'teal',
  defaultRadius: 'md',
  fontFamily: 'Segoe UI, Microsoft YaHei, sans-serif',
  headings: {
    fontFamily: 'Segoe UI Semibold, Microsoft YaHei, sans-serif',
  },
});

const strategyRows = [
  { name: 'BTC 网格增强', market: 'Binance / BTCUSDT', risk: '中', status: '运行中', pnl: '+12.4%' },
  { name: 'ETH 趋势跟随', market: 'OKX / ETHUSDT', risk: '高', status: '观察中', pnl: '+6.8%' },
  { name: 'SOL 回撤抄底', market: 'Bybit / SOLUSDT', risk: '高', status: '待审核', pnl: '-2.1%' },
];

const MantineComponentsTest: React.FC = () => {
  const navigate = useNavigate();
  const [colorScheme, setColorScheme] = useState<'light' | 'dark'>('light');
  const [budget, setBudget] = useState<number | string>(2500);
  const [notes, setNotes] = useState('这套演示页主要用于横向比较 Mantine 和当前 SnowUI 风格。');
  const [stackType, setStackType] = useState<string | null>('react-vite');
  const [modules, setModules] = useState<string[]>(['ui', 'notifications', 'hooks']);
  const [tags, setTags] = useState<string[]>(['量化', '策略托管', '推送提醒']);
  const [priority, setPriority] = useState('balanced');
  const [segmentedView, setSegmentedView] = useState('desktop');
  const [autoDeploy, setAutoDeploy] = useState(true);
  const [emailPush, setEmailPush] = useState(true);
  const [smsPush, setSmsPush] = useState(false);
  const [sliderValue, setSliderValue] = useState(48);
  const [rangeValue, setRangeValue] = useState<[number, number]>([18, 74]);
  const [accentColor, setAccentColor] = useState('#12b886');
  const [activeStep, setActiveStep] = useState(1);
  const [modalOpened, modalHandlers] = useDisclosure(false);
  const [drawerOpened, drawerHandlers] = useDisclosure(false);
  const [popoverOpened, setPopoverOpened] = useState(false);

  const pageStyle =
    colorScheme === 'dark'
      ? {
          background:
            'radial-gradient(circle at top left, rgba(20, 184, 166, 0.18), transparent 30%), radial-gradient(circle at top right, rgba(14, 116, 144, 0.22), transparent 35%), linear-gradient(180deg, #0f172a 0%, #111827 100%)',
        }
      : undefined;

  const toggleColorScheme = () => {
    setColorScheme((current) => (current === 'light' ? 'dark' : 'light'));
  };

  const showMantineNotification = () => {
    notifications.show({
      title: 'Mantine 通知示范',
      message: '这条消息来自 @mantine/notifications，可用于右上角浮层提醒。',
      color: 'teal',
      icon: <IconBellRinging size={16} />,
      autoClose: 3000,
    });
  };

  const strategyTableRows = strategyRows.map((item) => (
    <Table.Tr key={item.name}>
      <Table.Td>{item.name}</Table.Td>
      <Table.Td>{item.market}</Table.Td>
      <Table.Td>
        <Badge color={item.risk === '中' ? 'yellow' : 'red'} variant="light">
          {item.risk}
        </Badge>
      </Table.Td>
      <Table.Td>
        <Badge color={item.status === '运行中' ? 'teal' : item.status === '观察中' ? 'blue' : 'gray'} variant="light">
          {item.status}
        </Badge>
      </Table.Td>
      <Table.Td>{item.pnl}</Table.Td>
    </Table.Tr>
  ));

  return (
    <MantineProvider theme={mantineTheme} forceColorScheme={colorScheme}>
      <Notifications position="top-right" />

      <Modal
        opened={modalOpened}
        onClose={modalHandlers.close}
        title="Mantine Modal"
        centered
        size="lg"
      >
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            这一块用来模拟“配置预览”或“操作确认”类弹窗。Mantine 在排版、按钮层级和间距上默认比较完整，拿来做中后台弹窗很省力。
          </Text>
          <Alert color="teal" variant="light" title="适用场景">
            表单配置、二次确认、详情预览、侧边流程拆分前的轻量弹层。
          </Alert>
          <Code block>{`notifications.show({ title: '已保存', message: '使用 Mantine 右上角提示' })`}</Code>
          <Group justify="flex-end">
            <Button variant="default" onClick={modalHandlers.close}>
              关闭
            </Button>
            <Button onClick={showMantineNotification}>保存并提示</Button>
          </Group>
        </Stack>
      </Modal>

      <Drawer
        opened={drawerOpened}
        onClose={drawerHandlers.close}
        position="right"
        title="Mantine Drawer"
        padding="lg"
      >
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            Drawer 适合做筛选面板、消息中心、移动端导航或分步编辑器。
          </Text>
          <Button variant="light" leftSection={<IconChecklist size={16} />}>
            策略筛选条件
          </Button>
          <Button variant="light" leftSection={<IconUsers size={16} />}>
            用户分组管理
          </Button>
          <Button variant="light" leftSection={<IconDatabase size={16} />}>
            数据同步状态
          </Button>
          <Divider />
          <Text size="sm">
            当前推荐视角：
            <Text span fw={700} c="teal">
              {segmentedView === 'desktop' ? ' 桌面管理台' : segmentedView === 'tablet' ? ' 平板场景' : ' 移动端'}
            </Text>
          </Text>
        </Stack>
      </Drawer>

      <div className="mantine-test-page" style={pageStyle}>
        <div className="mantine-test-shell">
          <Paper className="mantine-test-hero" radius="xl" p="xl" shadow="sm" withBorder>
            <Stack gap="lg">
              <Group justify="space-between" align="flex-start" gap="lg">
                <Stack gap="md">
                  <Group gap="sm" wrap="wrap">
                    <Button
                      variant="default"
                      leftSection={<IconArrowLeft size={16} />}
                      onClick={() => {
                        navigate('/test');
                      }}
                    >
                      返回测试页
                    </Button>
                    <Badge color="teal" variant="light">
                      @mantine/core 8.3.16
                    </Badge>
                    <Badge color="blue" variant="outline">
                      React 19 可用
                    </Badge>
                    <Badge color="gray" variant="dot">
                      独立 UI 库示范页
                    </Badge>
                  </Group>

                  <div>
                    <Title order={1}>Mantine 组件库参考页</Title>
                    <Text mt="sm" c="dimmed">
                      这里单独展示 Mantine 的常见组件、状态组合和浮层能力，用来对比现有 SnowUI 页面风格，也方便你判断哪些交互适合直接借鉴。
                    </Text>
                  </div>

                  <Group gap="sm" wrap="wrap">
                    <Button component="a" href="#mantine-actions" variant="light" color="teal">
                      基础控件
                    </Button>
                    <Button component="a" href="#mantine-forms" variant="light" color="blue">
                      表单输入
                    </Button>
                    <Button component="a" href="#mantine-feedback" variant="light" color="grape">
                      反馈展示
                    </Button>
                    <Button component="a" href="#mantine-overlays" variant="light" color="orange">
                      浮层导航
                    </Button>
                  </Group>
                </Stack>

                <Stack gap="sm" align="flex-start">
                  <ActionIcon
                    size="lg"
                    variant="default"
                    aria-label="切换明暗主题"
                    onClick={toggleColorScheme}
                  >
                    {colorScheme === 'light' ? <IconMoonStars size={18} /> : <IconSunHigh size={18} />}
                  </ActionIcon>
                  <Button leftSection={<IconBellRinging size={16} />} onClick={showMantineNotification}>
                    触发通知
                  </Button>
                  <Button variant="default" leftSection={<IconArrowUpRight size={16} />} onClick={modalHandlers.open}>
                    打开 Modal
                  </Button>
                  <Text size="xs" c="dimmed">
                    当前主题：{colorScheme === 'light' ? '浅色' : '深色'}
                  </Text>
                </Stack>
              </Group>

              <Group gap="sm" wrap="wrap">
                <Badge variant="light" color="teal">
                  路由 <Code>/mantine-test</Code>
                </Badge>
                <Badge variant="light" color="cyan">
                  适合中后台
                </Badge>
                <Badge variant="light" color="lime">
                  默认样式完整
                </Badge>
                <Badge variant="light" color="indigo">
                  表单和浮层丰富
                </Badge>
              </Group>
            </Stack>
          </Paper>

          <Paper id="mantine-actions" className="mantine-demo-section" radius="xl" p="xl" shadow="sm" withBorder>
            <Stack gap="lg">
              <div>
                <Title order={2}>基础动作与品牌元素</Title>
                <Text mt="xs" c="dimmed">
                  先看 Mantine 最容易出效果的一层：按钮、徽章、主题图标、头像和轻量品牌模块。
                </Text>
              </div>

              <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="lg">
                <Card radius="lg" withBorder shadow="xs">
                  <Stack gap="md">
                    <div>
                      <Text fw={700}>Button / ActionIcon</Text>
                      <Text size="sm" c="dimmed">
                        常用变体、尺寸和图标组合。
                      </Text>
                    </div>

                    <Group wrap="wrap">
                      <Button>Filled</Button>
                      <Button variant="light">Light</Button>
                      <Button variant="outline">Outline</Button>
                      <Button variant="subtle">Subtle</Button>
                      <Button variant="default">Default</Button>
                      <Button variant="gradient" gradient={{ from: 'teal', to: 'lime', deg: 120 }}>
                        Gradient
                      </Button>
                    </Group>

                    <Group wrap="wrap">
                      <Button size="xs">XS</Button>
                      <Button size="sm">SM</Button>
                      <Button size="md">MD</Button>
                      <Button size="lg">LG</Button>
                    </Group>

                    <Group wrap="wrap">
                      <ActionIcon variant="filled" color="teal" size="lg">
                        <IconPlayerPlay size={18} />
                      </ActionIcon>
                      <ActionIcon variant="light" color="blue" size="lg">
                        <IconSearch size={18} />
                      </ActionIcon>
                      <ActionIcon variant="outline" color="grape" size="lg">
                        <IconSettings size={18} />
                      </ActionIcon>
                      <ActionIcon variant="subtle" color="orange" size="lg">
                        <IconPalette size={18} />
                      </ActionIcon>
                    </Group>
                  </Stack>
                </Card>

                <Card radius="lg" withBorder shadow="xs">
                  <Stack gap="md">
                    <div>
                      <Text fw={700}>Badge / ThemeIcon / Avatar</Text>
                      <Text size="sm" c="dimmed">
                        做状态提示、分组标签和轻量品牌识别很直接。
                      </Text>
                    </div>

                    <Group wrap="wrap">
                      <Badge color="teal">在线</Badge>
                      <Badge color="blue" variant="light">
                        API 已连接
                      </Badge>
                      <Badge color="yellow" variant="outline">
                        审核中
                      </Badge>
                      <Badge color="red" variant="dot">
                        风险较高
                      </Badge>
                    </Group>

                    <Group wrap="wrap">
                      <ThemeIcon size="lg" radius="xl" color="teal">
                        <IconBrandReact size={18} />
                      </ThemeIcon>
                      <ThemeIcon size="lg" radius="xl" color="blue" variant="light">
                        <IconCode size={18} />
                      </ThemeIcon>
                      <ThemeIcon size="lg" radius="xl" color="grape" variant="outline">
                        <IconShieldCheck size={18} />
                      </ThemeIcon>
                      <ThemeIcon
                        size="lg"
                        radius="xl"
                        variant="gradient"
                        gradient={{ from: 'cyan', to: 'teal', deg: 120 }}
                      >
                        <IconBolt size={18} />
                      </ThemeIcon>
                    </Group>

                    <Group justify="space-between" wrap="wrap">
                      <Avatar.Group spacing="sm">
                        <Avatar color="teal" radius="xl">
                          C
                        </Avatar>
                        <Avatar color="blue" radius="xl">
                          D
                        </Avatar>
                        <Avatar color="grape" radius="xl">
                          X
                        </Avatar>
                      </Avatar.Group>
                      <Text size="sm" c="dimmed">
                        Avatar.Group 很适合协作成员、策略订阅者或审批人列表。
                      </Text>
                    </Group>
                  </Stack>
                </Card>
              </SimpleGrid>
            </Stack>
          </Paper>

          <Paper id="mantine-forms" className="mantine-demo-section" radius="xl" p="xl" shadow="sm" withBorder>
            <Stack gap="lg">
              <div>
                <Title order={2}>表单与选择器</Title>
                <Text mt="xs" c="dimmed">
                  Mantine 在表单这一层很完整，输入框、选择器、标签输入和开关控件都比较成熟。
                </Text>
              </div>

              <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="lg">
                <Card radius="lg" withBorder shadow="xs">
                  <Stack gap="md">
                    <Text fw={700}>文本输入类</Text>
                    <TextInput
                      label="策略名称"
                      placeholder="例如：BTC 波段增强"
                      defaultValue="BTC 波段增强"
                      leftSection={<IconCode size={16} />}
                    />
                    <PasswordInput
                      label="API Secret"
                      placeholder="输入交易所密钥"
                      defaultValue="mantine-demo-secret"
                    />
                    <NumberInput
                      label="预算"
                      value={budget}
                      onChange={setBudget}
                      min={0}
                      step={100}
                      thousandSeparator="," 
                    />
                    <Textarea
                      label="说明"
                      minRows={4}
                      autosize
                      value={notes}
                      onChange={(event) => {
                        setNotes(event.currentTarget.value);
                      }}
                    />
                  </Stack>
                </Card>

                <Card radius="lg" withBorder shadow="xs">
                  <Stack gap="md">
                    <Text fw={700}>选择器与标签输入</Text>
                    <Select
                      label="项目模板"
                      data={[
                        { value: 'react-vite', label: 'React + Vite' },
                        { value: 'nextjs', label: 'Next.js' },
                        { value: 'electron', label: 'Electron' },
                      ]}
                      value={stackType}
                      onChange={setStackType}
                    />
                    <MultiSelect
                      label="已启用模块"
                      data={['ui', 'notifications', 'hooks', 'charts', 'forms']}
                      value={modules}
                      onChange={setModules}
                    />
                    <TagsInput label="标签" value={tags} onChange={setTags} placeholder="输入后回车新增" />
                    <ColorInput label="强调色" value={accentColor} onChange={setAccentColor} format="hex" />
                  </Stack>
                </Card>

                <Card radius="lg" withBorder shadow="xs">
                  <Stack gap="md">
                    <Text fw={700}>开关、单选与分段控制</Text>
                    <SegmentedControl
                      fullWidth
                      value={segmentedView}
                      onChange={setSegmentedView}
                      data={[
                        { label: '桌面', value: 'desktop' },
                        { label: '平板', value: 'tablet' },
                        { label: '移动', value: 'mobile' },
                      ]}
                    />

                    <Switch
                      label="发布后自动部署"
                      description="适合配置保存后直接刷新任务的场景"
                      checked={autoDeploy}
                      onChange={(event) => {
                        setAutoDeploy(event.currentTarget.checked);
                      }}
                    />

                    <Group grow>
                      <Checkbox
                        label="邮件提醒"
                        checked={emailPush}
                        onChange={(event) => {
                          setEmailPush(event.currentTarget.checked);
                        }}
                      />
                      <Checkbox
                        label="短信提醒"
                        checked={smsPush}
                        onChange={(event) => {
                          setSmsPush(event.currentTarget.checked);
                        }}
                      />
                    </Group>

                    <Radio.Group
                      label="默认优先级"
                      value={priority}
                      onChange={setPriority}
                    >
                      <Group mt="xs">
                        <Radio value="safe" label="稳健" />
                        <Radio value="balanced" label="平衡" />
                        <Radio value="aggressive" label="激进" />
                      </Group>
                    </Radio.Group>
                  </Stack>
                </Card>

                <Card radius="lg" withBorder shadow="xs">
                  <Stack gap="md">
                    <Text fw={700}>Slider / RangeSlider</Text>
                    <div>
                      <Group justify="space-between">
                        <Text size="sm">权限粒度</Text>
                        <Text size="sm" fw={700}>
                          {sliderValue}%
                        </Text>
                      </Group>
                      <Slider
                        mt="sm"
                        value={sliderValue}
                        onChange={setSliderValue}
                        marks={[
                          { value: 20, label: '20' },
                          { value: 50, label: '50' },
                          { value: 80, label: '80' },
                        ]}
                      />
                    </div>

                    <div>
                      <Group justify="space-between">
                        <Text size="sm">运行时间窗</Text>
                        <Text size="sm" fw={700}>
                          {rangeValue[0]} - {rangeValue[1]}
                        </Text>
                      </Group>
                      <RangeSlider
                        mt="sm"
                        value={rangeValue}
                        onChange={setRangeValue}
                        min={0}
                        max={100}
                        marks={[
                          { value: 0, label: '0' },
                          { value: 50, label: '50' },
                          { value: 100, label: '100' },
                        ]}
                      />
                    </div>
                  </Stack>
                </Card>
              </SimpleGrid>
            </Stack>
          </Paper>

          <Paper id="mantine-feedback" className="mantine-demo-section" radius="xl" p="xl" shadow="sm" withBorder>
            <Stack gap="lg">
              <div>
                <Title order={2}>反馈与数据展示</Title>
                <Text mt="xs" c="dimmed">
                  这一层更接近业务页面：提醒、通知条、卡片、表格、进度和时间线。
                </Text>
              </div>

              <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="lg">
                <Card radius="lg" withBorder shadow="xs">
                  <Stack gap="md">
                    <Text fw={700}>Alert / Notification</Text>
                    <Alert color="teal" variant="light" title="适合成功反馈" icon={<IconShieldCheck size={16} />}>
                      接口授权已通过，后续可以继续绑定交易所和推送策略。
                    </Alert>
                    <Alert color="yellow" variant="outline" title="适合提醒或阻断" icon={<IconBellRinging size={16} />}>
                      旧版 SnowUI 页面还在维护中，Mantine 更适合做新的工具型页面。
                    </Alert>
                    <MantineNotification
                      title="静态通知条"
                      color="blue"
                      icon={<IconDatabase size={16} />}
                      withCloseButton={false}
                    >
                      Notification 组件可以直接嵌进页面里，不一定非要走右上角浮层。
                    </MantineNotification>
                    <Button variant="light" onClick={showMantineNotification}>
                      再弹一条右上角通知
                    </Button>
                  </Stack>
                </Card>

                <Card radius="lg" withBorder shadow="xs">
                  <Stack gap="md">
                    <Text fw={700}>Card / Table</Text>
                    <Card radius="md" withBorder padding="md">
                      <Group justify="space-between">
                        <div>
                          <Text fw={700}>策略概览卡片</Text>
                          <Text size="sm" c="dimmed">
                            可直接嵌详情、统计和快捷动作。
                          </Text>
                        </div>
                        <ThemeIcon color="teal" variant="light" size="lg">
                          <IconBrandReact size={18} />
                        </ThemeIcon>
                      </Group>
                    </Card>

                    <Table withTableBorder withColumnBorders striped highlightOnHover>
                      <Table.Thead>
                        <Table.Tr>
                          <Table.Th>策略</Table.Th>
                          <Table.Th>市场</Table.Th>
                          <Table.Th>风险</Table.Th>
                          <Table.Th>状态</Table.Th>
                          <Table.Th>近 30 天</Table.Th>
                        </Table.Tr>
                      </Table.Thead>
                      <Table.Tbody>{strategyTableRows}</Table.Tbody>
                    </Table>
                  </Stack>
                </Card>

                <Card radius="lg" withBorder shadow="xs">
                  <Stack gap="md">
                    <Text fw={700}>Progress / RingProgress</Text>
                    <div>
                      <Group justify="space-between" mb="xs">
                        <Text size="sm">策略部署进度</Text>
                        <Text size="sm" fw={700}>
                          72%
                        </Text>
                      </Group>
                      <Progress value={72} size="lg" radius="xl" />
                    </div>

                    <Group justify="space-between" align="center">
                      <RingProgress
                        size={124}
                        roundCaps
                        sections={[
                          { value: 42, color: 'teal' },
                          { value: 28, color: 'blue' },
                          { value: 12, color: 'grape' },
                        ]}
                        label={
                          <Text ta="center" fw={700} size="lg">
                            82%
                          </Text>
                        }
                      />
                      <Stack gap={6}>
                        <Text size="sm">成功率</Text>
                        <Badge color="teal" variant="light">
                          回测通过
                        </Badge>
                        <Badge color="blue" variant="light">
                          告警已开启
                        </Badge>
                        <Badge color="grape" variant="light">
                          指标同步中
                        </Badge>
                      </Stack>
                    </Group>
                  </Stack>
                </Card>

                <Card radius="lg" withBorder shadow="xs">
                  <Stack gap="md">
                    <Text fw={700}>Timeline</Text>
                    <Timeline active={2} bulletSize={28} lineWidth={2}>
                      <Timeline.Item bullet={<IconCode size={14} />} title="配置表单">
                        <Text size="sm" c="dimmed">
                          输入交易所参数、风险限制与策略备注。
                        </Text>
                      </Timeline.Item>
                      <Timeline.Item bullet={<IconChecklist size={14} />} title="保存并校验">
                        <Text size="sm" c="dimmed">
                          调用接口检查字段、权限与账户绑定状态。
                        </Text>
                      </Timeline.Item>
                      <Timeline.Item bullet={<IconPlayerPlay size={14} />} title="开始运行">
                        <Text size="sm" c="dimmed">
                          托管任务创建完成，进入实时监控。
                        </Text>
                      </Timeline.Item>
                    </Timeline>
                  </Stack>
                </Card>
              </SimpleGrid>
            </Stack>
          </Paper>

          <Paper id="mantine-overlays" className="mantine-demo-section" radius="xl" p="xl" shadow="sm" withBorder>
            <Stack gap="lg">
              <div>
                <Title order={2}>导航、层叠与浮层</Title>
                <Text mt="xs" c="dimmed">
                  Mantine 在 Tabs、Accordion、Popover、Menu、Tooltip 这类交互里也比较省代码。
                </Text>
              </div>

              <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="lg">
                <Card radius="lg" withBorder shadow="xs">
                  <Stack gap="md">
                    <Text fw={700}>Tabs / Accordion</Text>
                    <Tabs defaultValue="overview">
                      <Tabs.List>
                        <Tabs.Tab value="overview">概览</Tabs.Tab>
                        <Tabs.Tab value="api">接口</Tabs.Tab>
                        <Tabs.Tab value="theme">主题</Tabs.Tab>
                      </Tabs.List>

                      <Tabs.Panel value="overview" pt="md">
                        <Text size="sm" c="dimmed">
                          Tabs 适合做详情页的分栏结构，默认样式已经比较规整。
                        </Text>
                      </Tabs.Panel>
                      <Tabs.Panel value="api" pt="md">
                        <Code block>{`<Notifications position="top-right" />`}</Code>
                      </Tabs.Panel>
                      <Tabs.Panel value="theme" pt="md">
                        <Text size="sm" c="dimmed">
                          `MantineProvider + createTheme` 可以统一颜色、圆角和字体。
                        </Text>
                      </Tabs.Panel>
                    </Tabs>

                    <Accordion variant="separated" radius="md">
                      <Accordion.Item value="one">
                        <Accordion.Control>为什么适合工具页？</Accordion.Control>
                        <Accordion.Panel>
                          表单多、反馈多、浮层多的页面，Mantine 能明显减少样式和状态胶水代码。
                        </Accordion.Panel>
                      </Accordion.Item>
                      <Accordion.Item value="two">
                        <Accordion.Control>什么时候不适合？</Accordion.Control>
                        <Accordion.Panel>
                          如果你需要完全贴近 SnowUI 既有视觉语言，就不能直接照搬默认样式。
                        </Accordion.Panel>
                      </Accordion.Item>
                    </Accordion>
                  </Stack>
                </Card>

                <Card radius="lg" withBorder shadow="xs">
                  <Stack gap="md">
                    <Text fw={700}>Stepper</Text>
                    <Stepper active={activeStep} onStepClick={setActiveStep}>
                      <Stepper.Step label="建表单" description="填策略参数">
                        <Text size="sm" c="dimmed">
                          先把主表单、校验和默认值搭好。
                        </Text>
                      </Stepper.Step>
                      <Stepper.Step label="接接口" description="保存与查询">
                        <Text size="sm" c="dimmed">
                          把策略保存、详情拉取和状态刷新接起来。
                        </Text>
                      </Stepper.Step>
                      <Stepper.Step label="加提示" description="通知与浮层">
                        <Text size="sm" c="dimmed">
                          再用通知、Drawer、Tooltip 补完交互细节。
                        </Text>
                      </Stepper.Step>
                      <Stepper.Completed>
                        <Text size="sm" c="dimmed">
                          这一页已经具备展示 Mantine 常见能力的参考价值。
                        </Text>
                      </Stepper.Completed>
                    </Stepper>

                    <Group justify="space-between">
                      <Button
                        variant="default"
                        onClick={() => {
                          setActiveStep((current) => Math.max(current - 1, 0));
                        }}
                      >
                        上一步
                      </Button>
                      <Button
                        onClick={() => {
                          setActiveStep((current) => Math.min(current + 1, 3));
                        }}
                      >
                        下一步
                      </Button>
                    </Group>
                  </Stack>
                </Card>

                <Card radius="lg" withBorder shadow="xs">
                  <Stack gap="md">
                    <Text fw={700}>Popover / Menu / Tooltip / HoverCard</Text>
                    <Group wrap="wrap">
                      <Popover
                        opened={popoverOpened}
                        onChange={setPopoverOpened}
                        width={260}
                        position="bottom-start"
                        withArrow
                        shadow="md"
                      >
                        <Popover.Target>
                          <Button variant="light" onClick={() => setPopoverOpened((current) => !current)}>
                            Popover
                          </Button>
                        </Popover.Target>
                        <Popover.Dropdown>
                          <Text size="sm" c="dimmed">
                            这里适合放轻量说明、辅助操作或筛选面板。
                          </Text>
                        </Popover.Dropdown>
                      </Popover>

                      <Menu shadow="md" width={220} withinPortal={false}>
                        <Menu.Target>
                          <Button variant="outline">Menu</Button>
                        </Menu.Target>
                        <Menu.Dropdown>
                          <Menu.Label>快速操作</Menu.Label>
                          <Menu.Item leftSection={<IconPlayerPlay size={14} />}>立即运行</Menu.Item>
                          <Menu.Item leftSection={<IconSettings size={14} />}>打开设置</Menu.Item>
                          <Menu.Item leftSection={<IconDatabase size={14} />}>查看日志</Menu.Item>
                        </Menu.Dropdown>
                      </Menu>

                      <Tooltip label="适合按钮说明、图标提示和状态解释" withArrow>
                        <Button variant="subtle">Tooltip</Button>
                      </Tooltip>

                      <HoverCard shadow="md" width={240} withinPortal={false}>
                        <HoverCard.Target>
                          <Button variant="default">HoverCard</Button>
                        </HoverCard.Target>
                        <HoverCard.Dropdown>
                          <Text size="sm" c="dimmed">
                            HoverCard 更适合悬停预览，例如用户信息、策略摘要或快捷统计。
                          </Text>
                        </HoverCard.Dropdown>
                      </HoverCard>
                    </Group>
                  </Stack>
                </Card>

                <Card radius="lg" withBorder shadow="xs">
                  <Stack gap="md">
                    <Text fw={700}>Modal / Drawer 入口</Text>
                    <Text size="sm" c="dimmed">
                      下面这两个入口和页面顶部的按钮共用状态，方便你直接看两种层叠容器的差异。
                    </Text>

                    <Group wrap="wrap">
                      <Button leftSection={<IconArrowUpRight size={16} />} onClick={modalHandlers.open}>
                        打开 Modal
                      </Button>
                      <Button variant="light" leftSection={<IconUsers size={16} />} onClick={drawerHandlers.open}>
                        打开 Drawer
                      </Button>
                    </Group>

                    <Divider />

                    <Text size="sm" c="dimmed">
                      当前视图：
                      <Text span fw={700} c="teal">
                        {segmentedView === 'desktop' ? ' 桌面' : segmentedView === 'tablet' ? ' 平板' : ' 移动'}
                      </Text>
                      ，强调色：
                      <Text span fw={700} c={accentColor}>
                        {' '}
                        {accentColor}
                      </Text>
                    </Text>
                  </Stack>
                </Card>
              </SimpleGrid>
            </Stack>
          </Paper>
        </div>
      </div>
    </MantineProvider>
  );
};

export default MantineComponentsTest;
