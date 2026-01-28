const PROFILE_KEY = "dwq_auth_profile";

export type AuthProfile = {
  email: string;
  nickname?: string | null;
  avatarUrl?: string | null;
  role?: number | null;
};

let inMemoryProfile: AuthProfile | null = null;

export function getAuthProfile(): AuthProfile | null {
  try {
    const stored = localStorage.getItem(PROFILE_KEY);
    if (stored) {
      const parsed = JSON.parse(stored) as AuthProfile;
      if (parsed && typeof parsed.email === "string") {
        inMemoryProfile = parsed;
        return parsed;
      }
    }
  } catch {
    // Ignore storage failures.
  }
  return inMemoryProfile;
}

export function setAuthProfile(profile: AuthProfile | null): void {
  inMemoryProfile = profile;
  try {
    if (profile) {
      localStorage.setItem(PROFILE_KEY, JSON.stringify(profile));
    } else {
      localStorage.removeItem(PROFILE_KEY);
    }
  } catch {
    // Ignore storage failures.
  }
}

export function clearAuthProfile(): void {
  setAuthProfile(null);
}
