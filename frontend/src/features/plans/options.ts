interface PlanOptionSource {
  id: string;
  applicationId: string;
  name: string;
  isEnabled: boolean;
}

export function planOptions(plans: readonly PlanOptionSource[] | undefined,
  applicationId: string | undefined, purpose: 'issue' | 'history') {
  const visible = plans?.filter(plan => plan.applicationId === applicationId &&
    (purpose === 'history' || plan.isEnabled)) ?? [];
  const counts = new Map<string, number>();
  for (const plan of visible) counts.set(plan.name, (counts.get(plan.name) ?? 0) + 1);
  return visible.map(plan => ({
    value: plan.id,
    label: (counts.get(plan.name) ?? 0) > 1
      ? plan.name + ' · ' + plan.id.slice(0, 8) : plan.name,
  }));
}
