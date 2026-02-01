import React from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { Layout, Menu, Button, Dropdown, Avatar, Space } from 'antd';
import { motion, AnimatePresence } from 'framer-motion';
import { 
  DashboardOutlined, 
  FileTextOutlined, 
  HistoryOutlined, 
  WifiOutlined,
  UserOutlined,
  LogoutOutlined,
  CloudServerOutlined
} from '@ant-design/icons';
import type { MenuProps } from 'antd';
import { clearToken } from '../network';
import { clearAuthProfile, getAuthProfile } from '../auth/profileStore';
import LogConsole from '../components/LogConsole';
import HistoricalData from './HistoricalData';
import NetworkStatus from './NetworkStatus';
import UniversalSearch from './UniversalSearch';
import ServerList from './ServerList';
import { PageTransition, FadeIn } from '../components/animations';
import './Dashboard.css';

const { Header, Sider, Content } = Layout;

const Dashboard: React.FC = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const profile = getAuthProfile();

  const handleLogout = () => {
    clearToken();
    clearAuthProfile();
    navigate('/login', { replace: true });
  };

  const menuItems: MenuProps['items'] = [
    {
      key: '/',
      icon: <DashboardOutlined />,
      label: '概览',
      onClick: () => navigate('/'),
    },
    {
      key: '/logs',
      icon: <FileTextOutlined />,
      label: '日志控制台',
      onClick: () => navigate('/logs'),
    },
    {
      key: '/historical-data',
      icon: <HistoryOutlined />,
      label: '历史行情',
      onClick: () => navigate('/historical-data'),
    },
    {
      key: '/network-status',
      icon: <WifiOutlined />,
      label: '网络详情',
      onClick: () => navigate('/network-status'),
    },
    {
      key: '/universal-search',
      icon: <UserOutlined />,
      label: '万向查询',
      onClick: () => navigate('/universal-search'),
    },
    {
      key: '/server-list',
      icon: <CloudServerOutlined />,
      label: '服务器列表',
      onClick: () => navigate('/server-list'),
    },
  ];

  const userMenuItems: MenuProps['items'] = [
    {
      key: 'logout',
      icon: <LogoutOutlined />,
      label: '退出登录',
      onClick: handleLogout,
    },
  ];

  return (
    <Layout style={{ minHeight: '100vh', background: 'var(--color-background)' }}>
      <motion.div
        initial={{ y: -100, opacity: 0 }}
        animate={{ y: 0, opacity: 1 }}
        transition={{ duration: 0.4, ease: [0.4, 0, 0.2, 1] }}
      >
        <Header style={{ 
          display: 'flex', 
          alignItems: 'center', 
          justifyContent: 'space-between',
          background: 'var(--color-primary)',
          padding: '0 24px',
          boxShadow: 'var(--shadow-md)',
          borderBottom: '1px solid rgba(255, 255, 255, 0.1)',
          position: 'sticky',
          top: 0,
          zIndex: 1030,
        }}
        >
        <motion.h1
          initial={{ opacity: 0, x: -20 }}
          animate={{ opacity: 1, x: 0 }}
          transition={{ delay: 0.1, duration: 0.4 }}
          style={{ 
            color: '#fff', 
            margin: 0, 
            fontSize: '18px', 
            fontWeight: 600,
            fontFamily: 'var(--font-heading)',
            letterSpacing: '-0.02em',
          }}
        >
          DWQuant 后台管理系统
        </motion.h1>
        <motion.div
          initial={{ opacity: 0, x: 20 }}
          animate={{ opacity: 1, x: 0 }}
          transition={{ delay: 0.2, duration: 0.4 }}
        >
          <Space size="middle">
            <motion.span
              whileHover={{ opacity: 1 }}
              style={{ 
                color: 'rgba(255, 255, 255, 0.85)', 
                fontSize: '14px',
                fontWeight: 400,
              }}
            >
              {profile?.email}
            </motion.span>
            <Dropdown 
              menu={{ items: userMenuItems }} 
              placement="bottomRight"
              trigger={['click']}
            >
              <motion.div
                whileHover={{ scale: 1.05 }}
                whileTap={{ scale: 0.95 }}
              >
                <Button 
                  type="text" 
                  icon={<Avatar size="small" icon={<UserOutlined />} style={{ backgroundColor: '#0369A1' }} />} 
                  style={{ 
                    color: '#fff',
                    padding: '4px 12px',
                    height: 'auto',
                    display: 'flex',
                    alignItems: 'center',
                    gap: '8px',
                  }}
                >
                  <span style={{ fontSize: '14px' }}>{profile?.email?.split('@')[0]}</span>
                </Button>
              </motion.div>
            </Dropdown>
          </Space>
        </motion.div>
      </Header>
      </motion.div>
      <Layout>
        <motion.div
          initial={{ x: -100, opacity: 0 }}
          animate={{ x: 0, opacity: 1 }}
          transition={{ delay: 0.15, duration: 0.4, ease: [0.4, 0, 0.2, 1] }}
        >
          <Sider 
            width={240} 
            style={{ 
              background: 'var(--color-surface)',
              boxShadow: 'var(--shadow-soft)',
              borderRight: '1px solid var(--color-border)',
            }}
            breakpoint="lg"
            collapsedWidth={80}
          >
            <Menu
              mode="inline"
              selectedKeys={[location.pathname]}
              items={menuItems}
              style={{ 
                height: '100%', 
                borderRight: 0,
                padding: '16px 8px',
                background: 'transparent',
              }}
            />
          </Sider>
        </motion.div>
        <Layout style={{ padding: '24px', background: 'transparent' }}>
          <Content
            style={{
              padding: 0,
              margin: 0,
              minHeight: 280,
              background: 'transparent',
            }}
          >
            <AnimatePresence mode="wait">
              {location.pathname === '/' && (
                <PageTransition key="home">
                  <motion.div
                    initial={{ scale: 0.95, opacity: 0 }}
                    animate={{ scale: 1, opacity: 1 }}
                    transition={{ duration: 0.3 }}
                    style={{
                      background: 'var(--color-surface)',
                      borderRadius: '12px',
                      padding: '32px',
                      boxShadow: 'var(--shadow-soft)',
                    }}
                  >
                    <FadeIn delay={0.1}>
                      <h2 style={{
                        fontFamily: 'var(--font-heading)',
                        fontSize: '24px',
                        fontWeight: 600,
                        color: 'var(--color-text)',
                        marginBottom: '8px',
                      }}>
                        欢迎使用后台管理系统
                      </h2>
                    </FadeIn>
                    <FadeIn delay={0.2}>
                      <p style={{
                        color: 'var(--color-text-secondary)',
                        fontSize: '14px',
                        margin: 0,
                      }}>
                        系统功能开发中...
                      </p>
                    </FadeIn>
                  </motion.div>
                </PageTransition>
              )}
              {location.pathname === '/logs' && (
                <PageTransition key="logs">
                  <LogConsole />
                </PageTransition>
              )}
              {location.pathname === '/historical-data' && (
                <PageTransition key="historical-data">
                  <HistoricalData />
                </PageTransition>
              )}
              {location.pathname === '/network-status' && (
                <PageTransition key="network-status">
                  <NetworkStatus />
                </PageTransition>
              )}
              {location.pathname === '/universal-search' && (
                <PageTransition key="universal-search">
                  <UniversalSearch />
                </PageTransition>
              )}
              {location.pathname === '/server-list' && (
                <PageTransition key="server-list">
                  <ServerList />
                </PageTransition>
              )}
            </AnimatePresence>
          </Content>
        </Layout>
      </Layout>
    </Layout>
  );
};

export default Dashboard;
