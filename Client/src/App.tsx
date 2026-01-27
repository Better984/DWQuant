import { useEffect } from 'react';
import { BrowserRouter as Router, Routes, Route, Navigate, useLocation, useNavigate } from 'react-router-dom';
import TestPage from './components/TestPage';
import Dashboard from './components/Dashboard';
import SnowUITest from './components/SnowUITest';
import UIComponentsTest from './components/UIComponentsTest';
import AuthPage from './components/AuthPage';
import KlineChartsDemo from './components/KlineChartsDemo';
import { clearToken, disconnectWs, getToken, onAuthExpired } from './network';
import { clearAuthProfile, getAuthProfile } from './auth/profileStore';
import { NotificationProvider } from './components/ui';
import WsNotificationBridge from './components/WsNotificationBridge';
import './App.css';

const RequireAuth: React.FC<{ children: React.ReactElement }> = ({ children }) => {
  const location = useLocation();
  const token = getToken();
  const profile = getAuthProfile();
  const from = `${location.pathname}${location.search}${location.hash}`;
  if (!token || !profile) {
    return <Navigate to="/auth" state={{ from }} replace />;
  }
  return children;
};

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
      if (location.pathname !== '/auth') {
        const from = `${location.pathname}${location.search}${location.hash}`;
        navigate('/auth', { replace: true, state: { from } });
      }
    };

    checkSession();
    const intervalId = window.setInterval(checkSession, 60000);
    const unsubscribe = onAuthExpired(checkSession);
    return () => {
      window.clearInterval(intervalId);
      unsubscribe();
    };
  }, [location.hash, location.pathname, location.search, navigate]);

  return null;
};

function App() {
  return (
    <NotificationProvider>
      <Router>
        <WsNotificationBridge />
        <SessionWatcher />
        <Routes>
          <Route path="/auth" element={<AuthPage />} />
          <Route path="/" element={<TestPage />} />
          <Route path="/test" element={<TestPage />} />
          <Route path="/dashboard" element={<RequireAuth><Dashboard /></RequireAuth>} />
          <Route path="/snowui-test" element={<SnowUITest />} />
          <Route path="/ui-components-test" element={<UIComponentsTest />} />
          <Route path="/klinecharts-demo" element={<KlineChartsDemo />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </Router>
    </NotificationProvider>
  );
}

export default App;
