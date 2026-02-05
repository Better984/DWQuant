import React, { useEffect, useMemo, useState } from 'react';
import { Button, Card, Form, Input, InputNumber, Modal, Select, Space, Table, Tabs, Tag, Typography } from 'antd';
import { ReloadOutlined, EditOutlined } from '@ant-design/icons';
import { HttpClient } from '../network/httpClient';
import { getToken } from '../network';
import { useNotification } from '../components/ui';
import RuntimeTemplates from './RuntimeTemplates';
import './ServerConfig.css';

const { Text } = Typography;
const { TextArea } = Input;

interface ServerConfigItem {
  key: string;
  category: string;
  valueType: string;
  value: string;
  description: string;
  isRealtime: boolean;
  updatedAt?: string;
}

const ServerConfig: React.FC = () => {
  const [items, setItems] = useState<ServerConfigItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [editing, setEditing] = useState<ServerConfigItem | null>(null);
  const [modalOpen, setModalOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [form] = Form.useForm<{ value: string | number | boolean }>();
  const client = new HttpClient();
  client.setTokenProvider(getToken);
  const { success, error: showError } = useNotification();

  useEffect(() => {
    loadConfigs();
  }, []);

  const loadConfigs = async () => {
    setLoading(true);
    try {
      const response = await client.postProtocol<{ items: ServerConfigItem[] }>(
        '/api/admin/server-config/list',
        'admin.server-config.list',
        {}
      );
      setItems(Array.isArray(response.items) ? response.items : []);
    } catch (err) {
      showError(err instanceof Error ? err.message : '加载配置失败');
    } finally {
      setLoading(false);
    }
  };

  const categories = useMemo(() => {
    const set = new Set(items.map((item) => item.category));
    return Array.from(set);
  }, [items]);

  const openEdit = (item: ServerConfigItem) => {
    setEditing(item);
    setModalOpen(true);
    form.setFieldsValue({
      value: parseValue(item),
    });
  };

  const closeModal = () => {
    setModalOpen(false);
    setEditing(null);
    form.resetFields();
  };

  const parseValue = (item: ServerConfigItem) => {
    switch (item.valueType) {
      case 'bool':
        return item.value === 'true';
      case 'int':
        return Number(item.value);
      case 'decimal':
        return Number(item.value);
      default:
        return item.value;
    }
  };

  const normalizeValue = (item: ServerConfigItem, value: any) => {
    if (item.valueType === 'bool') {
      return value ? 'true' : 'false';
    }
    if (item.valueType === 'int') {
      return Number(value).toString();
    }
    if (item.valueType === 'decimal') {
      return Number(value).toString();
    }
    return (value ?? '').toString();
  };

  const handleSubmit = async () => {
    if (!editing) {
      return;
    }

    try {
      const values = await form.validateFields();
      const normalized = normalizeValue(editing, values.value);
      setSaving(true);
      await client.postProtocol(
        '/api/admin/server-config/update',
        'admin.server-config.update',
        { key: editing.key, value: normalized }
      );
      success('更新成功');
      closeModal();
      await loadConfigs();
    } catch (err) {
      if (err instanceof Error) {
        showError(err.message);
      }
    } finally {
      setSaving(false);
    }
  };

  const renderValueInput = (item: ServerConfigItem | null) => {
    if (!item) {
      return null;
    }

    switch (item.valueType) {
      case 'bool':
        return (
          <Select
            options={[
              { label: 'true', value: true },
              { label: 'false', value: false },
            ]}
          />
        );
      case 'int':
        return <InputNumber style={{ width: '100%' }} />;
      case 'decimal':
        return <InputNumber style={{ width: '100%' }} step={0.01} />;
      case 'json':
        return <TextArea rows={6} />;
      default:
        return <Input />;
    }
  };

  const columns = [
    {
      title: '配置键',
      dataIndex: 'key',
      key: 'key',
      width: 260,
      render: (value: string) => <Text code>{value}</Text>,
    },
    {
      title: '值',
      dataIndex: 'value',
      key: 'value',
      width: 160,
      render: (value: string) => <Text>{value}</Text>,
    },
    {
      title: '类型',
      dataIndex: 'valueType',
      key: 'valueType',
      width: 100,
      render: (value: string) => <Tag>{value}</Tag>,
    },
    {
      title: '说明',
      dataIndex: 'description',
      key: 'description',
      render: (value: string) => <Text>{value}</Text>,
    },
    {
      title: '生效方式',
      dataIndex: 'isRealtime',
      key: 'isRealtime',
      width: 120,
      render: (value: boolean) => (
        <Tag color={value ? 'green' : 'default'}>{value ? '实时生效' : '需重启'}</Tag>
      ),
    },
    {
      title: '操作',
      key: 'action',
      width: 120,
      render: (_: unknown, record: ServerConfigItem) => (
        <Button size="small" icon={<EditOutlined />} onClick={() => openEdit(record)}>
          修改
        </Button>
      ),
    },
  ];

  const configTabItems = categories.map((category) => ({
    key: category,
    label: category,
    children: (
      <Table
        rowKey="key"
        dataSource={items.filter((item) => item.category === category)}
        columns={columns}
        pagination={false}
        loading={loading}
      />
    ),
  }));

  // 将所有Tab项合并，运行时间模板放在最前面
  const allTabItems = [
    {
      key: 'runtime-templates',
      label: '运行时间模板',
      children: <RuntimeTemplates />,
    },
    ...configTabItems,
  ];

  return (
    <div className="server-config">
      <Card className="server-config-card" bordered={false}>
        <div className="server-config-header">
          <div>
            <h2>服务器配置</h2>
            <p>配置项来自数据库，支持管理员调整，部分参数实时生效。</p>
          </div>
          <Space>
            <Button icon={<ReloadOutlined />} onClick={loadConfigs} loading={loading}>
              刷新
            </Button>
          </Space>
        </div>
        <Tabs items={allTabItems} />
      </Card>

      <Modal
        title={`修改配置：${editing?.key ?? ''}`}
        open={modalOpen}
        onCancel={closeModal}
        onOk={handleSubmit}
        confirmLoading={saving}
        width={520}
      >
        {editing && (
          <Form form={form} layout="vertical">
            <Form.Item label="说明">
              <Text>{editing.description}</Text>
            </Form.Item>
            <Form.Item label="生效方式">
              <Tag color={editing.isRealtime ? 'green' : 'default'}>
                {editing.isRealtime ? '实时生效' : '需重启'}
              </Tag>
            </Form.Item>
            <Form.Item
              label="配置值"
              name="value"
              rules={[{ required: true, message: '请输入配置值' }]}
            >
              {renderValueInput(editing)}
            </Form.Item>
          </Form>
        )}
      </Modal>
    </div>
  );
};

export default ServerConfig;
