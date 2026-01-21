import React, { useEffect } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import SignIn4 from './SignIn4';
import { getToken } from '../network';
import { getAuthProfile, type AuthProfile } from '../auth/profileStore';
import './AuthPage.css';

type LocationState = {
  from?: string;
};

const AuthPage: React.FC = () => {
  const navigate = useNavigate();
  const location = useLocation();

  useEffect(() => {
    if (getToken() && getAuthProfile()) {
      const state = location.state as LocationState | null;
      navigate(state?.from ?? '/', { replace: true });
    }
  }, [location.state, navigate]);

  const handleAuthenticated = (_profile: AuthProfile) => {
    const state = location.state as LocationState | null;
    navigate(state?.from ?? '/', { replace: true });
  };

  return (
    <div className="auth-page-container">
      <SignIn4 onAuthenticated={handleAuthenticated} />
    </div>
  );
};

export default AuthPage;
