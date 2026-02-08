const PROFILE_KEY = "dwq_auth_profile";
let inMemoryProfile = null;

export function getAuthProfile() {
  try {
    const stored = localStorage.getItem(PROFILE_KEY);
    if (stored) {
      const parsed = JSON.parse(stored);
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

export function setAuthProfile(profile) {
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

export function clearAuthProfile() {
  setAuthProfile(null);
}
