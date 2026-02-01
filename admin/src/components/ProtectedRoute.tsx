import React from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { getToken } from '../network';
import { getAuthProfile } from '../auth/profileStore';

const ADMIN_ROLE = 255;

interface ProtectedRouteProps {
  children: React.ReactNode;
}

const ProtectedRoute: React.FC<ProtectedRouteProps> = ({ children }) => {
  const location = useLocation();
  const token = getToken();
  const profile = getAuthProfile();

  // 检查是否已登录且角色为超级管理员
  if (!token || !profile || profile.role !== ADMIN_ROLE) {
    return <Navigate to="/login" state={{ from: location.pathname }} replace />;
  }

  return <>{children}</>;
};

export default ProtectedRoute;
