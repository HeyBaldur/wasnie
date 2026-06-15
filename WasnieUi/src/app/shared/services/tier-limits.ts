export interface TierLimitConfig {
  maxPlans: number;   // -1 = unlimited
  maxPayees: number;  // -1 = unlimited
}

export const TIER_LIMITS: Record<string, TierLimitConfig> = {
  Free:       { maxPlans: 1,  maxPayees: 5   },
  Starter:    { maxPlans: 5,  maxPayees: 25  },
  Growth:     { maxPlans: 15, maxPayees: 75  },
  Scale:      { maxPlans: -1, maxPayees: 150 },
  Enterprise: { maxPlans: -1, maxPayees: -1  },
};
