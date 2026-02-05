import React, { useEffect, useMemo, useState } from 'react';
import { Button, Card, Form, Input, Modal, Popconfirm, Select, Space, Table, Tag, Typography } from 'antd';
import { PlusOutlined, ReloadOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import { HttpClient } from '../network/httpClient';
import { getToken } from '../network';
import { useNotification } from '../components/ui';
import './RuntimeTemplates.css';

const { Text } = Typography;

interface StrategyRuntimeTimeRange {
  start: string;
  end: string;
}

interface StrategyRuntimeCalendarException {
  date: string;
  type: string;
  name?: string;
  timeRanges: StrategyRuntimeTimeRange[];
}

interface StrategyRuntimeTemplate {
  id: string;
  name: string;
  timezone: string;
  days: string[];
  timeRanges: StrategyRuntimeTimeRange[];
  calendar?: StrategyRuntimeCalendarException[];
}

interface TimezoneOption {
  value: string;
  label: string;
}

interface TemplateFormValues {
  id: string;
  name: string;
  timezone: string;
  days: string[];
  timeRanges: StrategyRuntimeTimeRange[];
  calendar?: StrategyRuntimeCalendarException[];
}

const WEEKDAY_OPTIONS = [
  { value: 'mon', label: '周一' },
  { value: 'tue', label: '周二' },
  { value: 'wed', label: '周三' },
  { value: 'thu', label: '周四' },
  { value: 'fri', label: '周五' },
  { value: 'sat', label: '周六' },
  { value: 'sun', label: '周日' },
];

const CALENDAR_TYPE_OPTIONS = [
  { value: 'Closed', label: '休市' },
  { value: 'Override', label: '覆盖' },
  { value: 'Append', label: '追加' },
];

const DAY_LABEL_MAP: Record<string, string> = {
  mon: '周一',
  tue: '周二',
  wed: '周三',
  thu: '周四',
  fri: '周五',
  sat: '周六',
  sun: '周日',
};

const RuntimeTemplates: React.FC = () => {
  const [templates, setTemplates] = useState<StrategyRuntimeTemplate[]>([]);
  const [timezones, setTimezones] = useState<TimezoneOption[]>([]);
  const [loading, setLoading] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<StrategyRuntimeTemplate | null>(null);
  const [saving, setSaving] = useState(false);
  const [form] = Form.useForm<TemplateFormValues>();
  const client = new HttpClient();
  client.setTokenProvider(getToken);
  const { success, error: showError } = useNotification();

  useEffect(() => {
    loadTemplates();
  }, []);

  const loadTemplates = async () => {
    setLoading(true);
    try {
      const response = await client.postProtocol<{ templates: StrategyRuntimeTemplate[]; timezones: TimezoneOption[] }>(
        '/api/admin/runtime-template/list',
        'admin.runtime-template.list',
        {}
      );
      setTemplates(Array.isArray(response.templates) ? response.templates : []);
      setTimezones(Array.isArray(response.timezones) ? response.timezones : []);
    } catch (err) {
      showError(err instanceof Error ? err.message : '加载模板失败');
    } finally {
      setLoading(false);
    }
  };

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    form.setFieldsValue({
      timezone: timezones[0]?.value ?? 'UTC',
      days: [],
      timeRanges: [{ start: '09:30', end: '16:00' }],
      calendar: [],
    });
    setModalOpen(true);
  };

  const openEdit = (template: StrategyRuntimeTemplate) => {
    setEditing(template);
    form.setFieldsValue({
      id: template.id,
      name: template.name,
      timezone: template.timezone,
      days: template.days ?? [],
      timeRanges: template.timeRanges ?? [],
      calendar: template.calendar ?? [],
    });
    setModalOpen(true);
  };

  const closeModal = () => {
    setModalOpen(false);
    setEditing(null);
    form.resetFields();
  };

  const buildPayload = (values: TemplateFormValues): StrategyRuntimeTemplate | null => {
    const timeRanges = (values.timeRanges || []).filter((range) => range?.start && range?.end);
    if (timeRanges.length === 0) {
      showError('请至少配置一个时间段');
      return null;
    }

    if (!values.days || values.days.length === 0) {
      showError('请至少选择一个星期');
      return null;
    }

    const calendar = (values.calendar || [])
      .filter((item) => item && item.date)
      .map((item) => ({
        date: item.date.trim(),
        type: item.type || 'Closed',
        name: item.name?.trim() || undefined,
        timeRanges: item.type === 'Closed'
          ? []
          : (item.timeRanges || []).filter((range) => range?.start && range?.end),
      }));

    return {
      id: values.id?.trim(),
      name: values.name?.trim(),
      timezone: values.timezone,
      days: values.days,
      timeRanges,
      calendar,
    };
  };

  const handleSubmit = async () => {
    try {
      const values = await form.validateFields();
      const payload = buildPayload(values);
      if (!payload) {
        return;
      }

      setSaving(true);
      if (editing) {
        await client.postProtocol(
          '/api/admin/runtime-template/update',
          'admin.runtime-template.update',
          { template: payload }
        );
        success('更新成功');
      } else {
        await client.postProtocol(
          '/api/admin/runtime-template/create',
          'admin.runtime-template.create',
          { template: payload }
        );
        success('新增成功');
      }

      closeModal();
      await loadTemplates();
    } catch (err) {
      if (err instanceof Error) {
        showError(err.message);
      }
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async (templateId: string) => {
    try {
      await client.postProtocol(
        '/api/admin/runtime-template/delete',
        'admin.runtime-template.delete',
        { templateId }
      );
      success('删除成功');
      await loadTemplates();
    } catch (err) {
      showError(err instanceof Error ? err.message : '删除失败');
    }
  };

  const timezoneOptions = useMemo(() => {
    if (timezones.length === 0) {
      return [
        { value: 'Asia/Shanghai', label: '中国/上海 (UTC+8)' },
        { value: 'America/New_York', label: '美国/纽约 (UTC-5/-4)' },
        { value: 'UTC', label: 'UTC' },
      ];
    }
    return timezones;
  }, [timezones]);

  const columns = [
    {
      title: '模板 ID',
      dataIndex: 'id',
      key: 'id',
      width: 200,
    },
    {
      title: '名称',
      dataIndex: 'name',
      key: 'name',
      width: 200,
      render: (value: string) => <Text strong>{value}</Text>,
    },
    {
      title: '时区',
      dataIndex: 'timezone',
      key: 'timezone',
      width: 180,
    },
    {
      title: '星期',
      dataIndex: 'days',
      key: 'days',
      render: (days: string[]) => (
        <Space wrap>
          {(days || []).map((day) => (
            <Tag key={day}>{DAY_LABEL_MAP[day] ?? day}</Tag>
          ))}
        </Space>
      ),
    },
    {
      title: '时间段',
      dataIndex: 'timeRanges',
      key: 'timeRanges',
      render: (ranges: StrategyRuntimeTimeRange[]) => (
        <Space wrap>
          {(ranges || []).map((range, index) => (
            <Tag key={`${range.start}-${range.end}-${index}`}>{`${range.start}-${range.end}`}</Tag>
          ))}
        </Space>
      ),
    },
    {
      title: '日历异常',
      dataIndex: 'calendar',
      key: 'calendar',
      width: 120,
      render: (calendar: StrategyRuntimeCalendarException[]) => (
        <Tag color={(calendar || []).length > 0 ? 'blue' : 'default'}>
          {(calendar || []).length} 条
        </Tag>
      ),
    },
    {
      title: '操作',
      key: 'action',
      width: 160,
      render: (_: unknown, record: StrategyRuntimeTemplate) => (
        <Space>
          <Button size="small" icon={<EditOutlined />} onClick={() => openEdit(record)}>
            编辑
          </Button>
          <Popconfirm
            title="确认删除该模板？"
            onConfirm={() => handleDelete(record.id)}
            okText="删除"
            cancelText="取消"
          >
            <Button danger size="small" icon={<DeleteOutlined />}>
              删除
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <div className="runtime-templates">
      <Card className="runtime-templates-card" bordered={false}>
        <div className="runtime-templates-header">
          <div>
            <h2>运行时间模板</h2>
            <p>维护交易时间模板与日历异常，策略仅保存模板 ID。</p>
          </div>
          <Space>
            <Button icon={<ReloadOutlined />} onClick={loadTemplates} loading={loading}>
              刷新
            </Button>
            <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
              新增模板
            </Button>
          </Space>
        </div>
        <Table
          rowKey="id"
          loading={loading}
          dataSource={templates}
          columns={columns}
          pagination={{ pageSize: 10 }}
        />
      </Card>

      <Modal
        title={editing ? '编辑运行时间模板' : '新增运行时间模板'}
        open={modalOpen}
        onCancel={closeModal}
        onOk={handleSubmit}
        confirmLoading={saving}
        width={920}
        className="runtime-templates-modal"
      >
        <Form form={form} layout="vertical">
          <div className="runtime-templates-form-grid">
            <Form.Item
              label="模板 ID"
              name="id"
              rules={[{ required: true, message: '请输入模板 ID' }]}
            >
              <Input disabled={!!editing} placeholder="如 cn.a_share.regular" />
            </Form.Item>
            <Form.Item
              label="模板名称"
              name="name"
              rules={[{ required: true, message: '请输入模板名称' }]}
            >
              <Input placeholder="如 A股交易时间" />
            </Form.Item>
            <Form.Item
              label="时区"
              name="timezone"
              rules={[{ required: true, message: '请选择时区' }]}
            >
              <Select options={timezoneOptions} />
            </Form.Item>
            <Form.Item
              label="星期"
              name="days"
              rules={[{ required: true, message: '请选择星期' }]}
            >
              <Select mode="multiple" allowClear options={WEEKDAY_OPTIONS} placeholder="选择生效星期" />
            </Form.Item>
          </div>

          <Card size="small" title="时间段" className="runtime-templates-section">
            <Form.List name="timeRanges">
              {(fields, { add, remove }) => (
                <>
                  {fields.map((field) => (
                    <Space key={field.key} align="baseline" className="runtime-templates-range">
                      <Form.Item
                        {...field}
                        name={[field.name, 'start']}
                        rules={[{ required: true, message: '开始时间' }]}
                      >
                        <Input placeholder="09:30" />
                      </Form.Item>
                      <span className="range-separator">-</span>
                      <Form.Item
                        {...field}
                        name={[field.name, 'end']}
                        rules={[{ required: true, message: '结束时间' }]}
                      >
                        <Input placeholder="16:00" />
                      </Form.Item>
                      <Button danger onClick={() => remove(field.name)}>
                        删除
                      </Button>
                    </Space>
                  ))}
                  <Button type="dashed" onClick={() => add({ start: '', end: '' })}>
                    新增时间段
                  </Button>
                </>
              )}
            </Form.List>
          </Card>

          <Card size="small" title="日历异常" className="runtime-templates-section">
            <Text type="secondary" className="runtime-templates-hint">
              Closed 类型可不配置时间段，Override/Append 需配置时间段。
            </Text>
            <Form.List name="calendar">
              {(fields, { add, remove }) => (
                <>
                  {fields.map((field) => (
                    <Card key={field.key} size="small" className="runtime-templates-calendar-item">
                      <Space align="baseline" className="runtime-templates-calendar-header">
                        <Form.Item
                          {...field}
                          name={[field.name, 'date']}
                          rules={[{ required: true, message: '日期必填' }]}
                        >
                          <Input placeholder="2026-02-16" />
                        </Form.Item>
                        <Form.Item
                          {...field}
                          name={[field.name, 'type']}
                          rules={[{ required: true, message: '类型必填' }]}
                          initialValue="Closed"
                        >
                          <Select options={CALENDAR_TYPE_OPTIONS} />
                        </Form.Item>
                        <Form.Item {...field} name={[field.name, 'name']}>
                          <Input placeholder="说明" />
                        </Form.Item>
                        <Button danger onClick={() => remove(field.name)}>
                          删除
                        </Button>
                      </Space>

                      <Form.List name={[field.name, 'timeRanges']}>
                        {(rangeFields, { add: addRange, remove: removeRange }) => (
                          <>
                            {rangeFields.map((rangeField) => (
                              <Space key={rangeField.key} align="baseline" className="runtime-templates-range">
                                <Form.Item
                                  {...rangeField}
                                  name={[rangeField.name, 'start']}
                                  rules={[{ required: true, message: '开始时间' }]}
                                >
                                  <Input placeholder="09:30" />
                                </Form.Item>
                                <span className="range-separator">-</span>
                                <Form.Item
                                  {...rangeField}
                                  name={[rangeField.name, 'end']}
                                  rules={[{ required: true, message: '结束时间' }]}
                                >
                                  <Input placeholder="16:00" />
                                </Form.Item>
                                <Button danger onClick={() => removeRange(rangeField.name)}>
                                  删除
                                </Button>
                              </Space>
                            ))}
                            <Button type="dashed" onClick={() => addRange({ start: '', end: '' })}>
                              新增日历时间段
                            </Button>
                          </>
                        )}
                      </Form.List>
                    </Card>
                  ))}
                  <Button type="dashed" onClick={() => add({ type: 'Closed', timeRanges: [] })}>
                    新增日历异常
                  </Button>
                </>
              )}
            </Form.List>
          </Card>
        </Form>
      </Modal>
    </div>
  );
};

export default RuntimeTemplates;
