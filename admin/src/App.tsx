import { useEffect } from 'react';
import { BrowserRouter, Routes, Route, Navigate, useLocation, useNavigate } from 'react-router-dom';
import { ConfigProvider } from 'antd';
import zhCN from 'antd/locale/zh_CN';
import { NotificationProvider } from './components/ui';
import AuthPage from './components/AuthPage';
import ProtectedRoute from './components/ProtectedRoute';
import Dashboard from './pages/Dashboard';
import { clearToken, disconnectWs, getToken, onAuthExpired } from './network';
import { clearAuthProfile, getAuthProfile } from './auth/profileStore';
import './App.css';

const SessionWatcher: React.FC = () => {
  const navigate = useNavigate();
  const location = useLocation();

  useEffect(() => {
    const checkSession = () => {
      const token = getToken();
      const profile = getAuthProfile();
      if (token && profile) {
        return;
      }

      clearToken();
      clearAuthProfile();
      disconnectWs();
      if (location.pathname !== '/login') {
        const from = `${location.pathname}${location.search}${location.hash}`;
        navigate('/login', { replace: true, state: { from } });
      }
    };

    checkSession();
    const unsubscribe = onAuthExpired(checkSession);
    return () => {
      unsubscribe();
    };
  }, [location.hash, location.pathname, location.search, navigate]);

  return null;
};

function App() {
  return (
    <ConfigProvider
      locale={zhCN}
      theme={{
        token: {
          // 主色调 - 专业蓝色
          colorPrimary: '#0369A1',
          colorSuccess: '#10B981',
          colorWarning: '#F59E0B',
          colorError: '#EF4444',
          colorInfo: '#0369A1',
          
          // 文字颜色
          colorText: '#020617',
          colorTextSecondary: '#64748B',
          colorTextTertiary: '#94A3B8',
          
          // 背景颜色
          colorBgContainer: '#FFFFFF',
          colorBgElevated: '#FFFFFF',
          colorBgLayout: '#F8FAFC',
          
          // 边框
          colorBorder: '#E2E8F0',
          colorBorderSecondary: '#F1F5F9',
          
          // 圆角
          borderRadius: 8,
          borderRadiusLG: 12,
          borderRadiusSM: 6,
          
          // 字体
          fontFamily: 'var(--font-body)',
          fontFamilyCode: 'var(--font-heading)',
          
          // 阴影 - Soft UI Evolution
          boxShadow: '0 2px 4px rgba(15, 23, 42, 0.04), 0 1px 2px rgba(15, 23, 42, 0.06)',
          boxShadowSecondary: '0 4px 6px -1px rgba(15, 23, 42, 0.1), 0 2px 4px -1px rgba(15, 23, 42, 0.06)',
          
          // 动画
          motionDurationFast: '150ms',
          motionDurationMid: '200ms',
          motionDurationSlow: '300ms',
        },
        components: {
          Layout: {
            bodyBg: '#F8FAFC',
            headerBg: '#0F172A',
            // 顶部导航条略微收窄
            headerHeight: 56,
            headerPadding: '0 16px',
            siderBg: '#FFFFFF',
          },
          Menu: {
            itemBg: 'transparent',
            itemHoverBg: '#F1F5F9',
            itemSelectedBg: '#E0F2FE',
            itemSelectedColor: '#0369A1',
            itemActiveBg: '#E0F2FE',
            itemMarginInline: 8,
            borderRadius: 8,
          },
          Button: {
            borderRadius: 8,
            controlHeight: 40,
            fontWeight: 500,
          },
          Input: {
            borderRadius: 8,
            controlHeight: 40,
          },
          Card: {
            borderRadius: 12,
            boxShadow: '0 2px 4px rgba(15, 23, 42, 0.04), 0 1px 2px rgba(15, 23, 42, 0.06)',
          },
        },
      }}
    >
      <NotificationProvider>
        <BrowserRouter>
          <SessionWatcher />
          <Routes>
            <Route path="/login" element={<AuthPage />} />
            <Route
              path="/"
              element={
                <ProtectedRoute>
                  <Dashboard />
                </ProtectedRoute>
              }
            />
            <Route
              path="/logs"
              element={
                <ProtectedRoute>
                  <Dashboard />
                </ProtectedRoute>
              }
            />
            <Route
              path="/historical-data"
              element={
                <ProtectedRoute>
                  <Dashboard />
                </ProtectedRoute>
              }
            />
            <Route
              path="/network-status"
              element={
                <ProtectedRoute>
                  <Dashboard />
                </ProtectedRoute>
              }
            />
            <Route
              path="/universal-search"
              element={
                <ProtectedRoute>
                  <Dashboard />
                </ProtectedRoute>
              }
            />
            <Route
              path="/server-list"
              element={
                <ProtectedRoute>
                  <Dashboard />
                </ProtectedRoute>
              }
            />
            <Route
              path="/server-config"
              element={
                <ProtectedRoute>
                  <Dashboard />
                </ProtectedRoute>
              }
            />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </BrowserRouter>
      </NotificationProvider>
    </ConfigProvider>
  );
}

export default App;
