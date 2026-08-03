import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormArray,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { distinctUntilChanged } from 'rxjs';
import { extractApiError } from '../../../shared/utils/api-error';
import { toSignal } from '@angular/core/rxjs-interop';
import { DecimalPipe, LowerCasePipe } from '@angular/common';
import { TranslateModule, TranslatePipe } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { PlansStore } from '../state/plans.store';
import { PlansApiService } from '../services/plans.api.service';
import { ToastService } from '../../../shared/services/toast.service';
import { getPlanPermissions } from '../services/plan-permissions';
import {
  CapScope,
  ConditionOperator,
  ConditionValueType,
  LogicalOperator,
  MeasurementAggregation,
  MeasurementType,
  ModifierType,
  RateTableType,
} from '../models/rule.model';
import { AddRuleRequest, TriggerField, UpdateRuleRequest } from '../models/rule.model';
import {
  WsPageHeaderComponent,
  WsButtonComponent,
  WsInputComponent,
  WsSelectComponent,
  WsCategoryPickerComponent,
  type SelectOption,
} from '../../../shared/ui';
import { WsTooltipDirective } from '../../../shared/ui/ws-tooltip/ws-tooltip.directive';

@Component({
  selector: 'app-rule-form',
  standalone: true,
  imports: [
    AppShellComponent,
    IconComponent,
    RouterLink,
    ReactiveFormsModule,
    TranslateModule,
    TranslatePipe,
    DecimalPipe,
    LowerCasePipe,
    WsPageHeaderComponent,
    WsButtonComponent,
    WsInputComponent,
    WsSelectComponent,
    WsCategoryPickerComponent,
    WsTooltipDirective,
  ],
  templateUrl: './rule-form.component.html',
  styleUrl: './rule-form.component.scss',
})
export class RuleFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly store = inject(PlansStore);
  private readonly toast = inject(ToastService);
  private readonly plansApi = inject(PlansApiService);

  private readonly destroyRef = inject(DestroyRef);

  readonly planId = this.route.snapshot.paramMap.get('planId')!;
  readonly ruleId = this.route.snapshot.paramMap.get('ruleId') ?? null;
  readonly isEdit = !!this.ruleId;
  readonly saving = signal(false);
  readonly readOnly = signal(false);

  readonly MeasurementType = MeasurementType;
  readonly MeasurementAggregation = MeasurementAggregation;
  readonly RateTableType = RateTableType;
  readonly ModifierType = ModifierType;
  readonly CapScope = CapScope;
  readonly LogicalOperator = LogicalOperator;
  readonly ConditionOperator = ConditionOperator;
  readonly ConditionValueType = ConditionValueType;

  // V1: only Revenue and Units are supported. Margin, Attainment, and Custom
  // require additional transaction fields — activate in a future WI.
  // FILTER MUST apply to every MeasurementType picker surface; see 14-forbidden-patterns.md.
  readonly measurementTypeOptions: SelectOption[] = [
    { label: 'PLANS.MEASUREMENT_REVENUE', value: MeasurementType.Revenue },
    { label: 'PLANS.MEASUREMENT_UNITS', value: MeasurementType.Units },
  ];

  // Aggregation is not yet implemented in the motor — Sum is the only valid option.
  // Average/Max/Min/Count are hidden from the UI to prevent silent misconfiguration.
  readonly aggregationOptions: SelectOption[] = [
    { label: 'PLANS.AGGREGATION_SUM', value: MeasurementAggregation.Sum },
  ];

  /** True when Measurement Type = Units is selected. Drives conditional UI rendering. */
  readonly isUnitsMode = computed(() =>
    Number(this.formValue()?.measurement?.type ?? MeasurementType.Revenue) === MeasurementType.Units
  );

  /**
   * The flat rate as the engine will read it, expressed the way a person thinks about it.
   *
   * ★ THE ENGINE'S TRUTH, CHECKED AND NOT ASSUMED: `CommissionCalculator` does
   * `baseAmount.Multiply(rateTable.FlatRate)`. The stored number is a MULTIPLIER, so 0.05 is 5% — and
   * 100 would be ten thousand per cent, not "one hundred per cent". The static hint has always said
   * so; a hint is also the easiest thing on a form to skim past, which is why this echoes back what
   * THIS value means as it is typed.
   *
   * Null in Units mode, where the same field is euros per unit and a percentage would be nonsense.
   */
  readonly flatRatePercent = computed<number | null>(() => {
    if (this.isUnitsMode()) {
      return null;
    }

    const raw = this.formValue()?.rateTable?.flatRate;
    return raw === null || raw === undefined || raw === '' as unknown as number || Number.isNaN(Number(raw))
      ? null
      : Number(raw) * 100;
  });

  /**
   * True when the rate would pay out the whole transaction or more.
   *
   * ★ THE THRESHOLD IS 100%, AND IT WARNS RATHER THAN BLOCKS. A multiplier of 1 or above means the
   * commission equals or exceeds the sale itself — which is what typing `100` for "one hundred per
   * cent" produces here, and it produces ten thousand per cent. That is the money error this exists to
   * catch.
   *
   * It does NOT block, because the value is not impossible: a referral rule paying the entire amount
   * is unusual but real, and a form that refuses a legitimate configuration sends the user to
   * support. Below 1 nothing is said at all — 0.5 is a high rate, not a mistake, and a warning that
   * fires on ordinary values is one people learn to click past.
   */
  readonly flatRateLooksMistaken = computed(() => {
    const percent = this.flatRatePercent();
    return percent !== null && percent >= 100;
  });

  readonly rateTableTypes = Object.entries(RateTableType)
    .filter(([, v]) => typeof v === 'number')
    .map(([k, v]) => ({ label: `PLANS.RATE_TABLE_${k.toUpperCase()}`, value: v as number }));

  readonly modifierTypeOptions: SelectOption[] = Object.entries(ModifierType)
    .filter(([, v]) => typeof v === 'number')
    .map(([k, v]) => ({ label: `PLANS.MODIFIER_${k.toUpperCase()}`, value: v as number }));

  // Only Per Transaction is honored by the engine today; Per Period / Total exist backend-side
  // but are deferred, so we never offer an option that would silently do nothing.
  readonly capScopeOptions: SelectOption[] = [
    { label: 'PLANS.CAP_SCOPE_PERTRANSACTION', value: CapScope.PerTransaction },
  ];

  // ── Trigger fields ────────────────────────────────────────────────────────────────────────
  // Fetched from the engine's catalog rather than declared here. A second copy in the browser is
  // exactly how the form came to offer field names the engine had never heard of, producing rules
  // that saved cleanly and then silently never fired.
  readonly triggerFields = signal<TriggerField[]>([]);

  // ── Category values ───────────────────────────────────────────────────────────────────────
  // A condition on `category` picks its value from the tenant's real categories rather than free text,
  // so a typo ("Laptps") can no longer save a rule that silently never fires. The list is short and
  // stable by design (one per business line), which is exactly why a picker is viable.
  readonly CATEGORY_FIELD = 'category';
  readonly categoryValues = signal<string[]>([]);
  readonly categoryValuesLoaded = signal(false);

  /** The tenant's categories as select options (value === label — category names aren't i18n keys). */
  readonly categoryOptions = computed<SelectOption[]>(() =>
    this.categoryValues().map(c => ({ value: c, label: c }))
  );

  readonly fieldOptions = computed<SelectOption[]>(() =>
    this.triggerFields().map(f => ({
      // Labels stay translatable; the LIST is the backend's.
      label: `PLANS.TRIGGER_FIELD_${f.field.toUpperCase()}`,
      value: f.field,
    }))
  );

  /** Operators this field's evaluator genuinely implements — never the full enum. */
  operatorOptionsFor(index: number): SelectOption[] {
    const definition = this._definitionAt(index);
    if (!definition) return [];
    return definition.operators.map(op => ({
      label: `PLANS.COND_OP_${op.operator.toUpperCase()}`,
      value: ConditionOperator[op.operator as keyof typeof ConditionOperator] as number,
    }));
  }

  /** True when the selected operator reads a LIST (In/NotIn) instead of a single value. */
  usesSet(index: number): boolean {
    const definition = this._definitionAt(index);
    const operator = Number(this.conditionsArray.at(index).get('operator')?.value);
    return definition?.operators.some(
      op => op.usesSet && ConditionOperator[op.operator as keyof typeof ConditionOperator] === operator
    ) ?? false;
  }

  /** A stored condition whose field is not in the catalog — it can never match. */
  isUnknownField(index: number): boolean {
    const field = this.fieldValueAt(index);
    if (!field || this.triggerFields().length === 0) return false;
    return !this.triggerFields().some(f => f.field.toLowerCase() === field.toLowerCase());
  }

  fieldValueAt(index: number): string {
    return this.conditionsArray.at(index).get('field')?.value ?? '';
  }

  private _definitionAt(index: number): TriggerField | undefined {
    const field = this.fieldValueAt(index);
    return this.triggerFields().find(f => f.field.toLowerCase() === field.toLowerCase());
  }

  // ── Category value picker ─────────────────────────────────────────────────────────────────
  // Only the `category` field is affected; every other field keeps its existing value input.

  isCategoryField(index: number): boolean {
    return this.fieldValueAt(index).toLowerCase() === this.CATEGORY_FIELD;
  }

  customValueAt(index: number): boolean {
    return !!this.conditionsArray.at(index).get('customValue')?.value;
  }

  valueRawAt(index: number): string {
    return this.conditionsArray.at(index).get('valueRaw')?.value ?? '';
  }

  valueSetAt(index: number): string {
    return this.conditionsArray.at(index).get('valueSet')?.value ?? '';
  }

  private _parseSet(csv: string): string[] {
    return String(csv ?? '').split(',').map(s => s.trim()).filter(s => s.length > 0);
  }

  /** True when the value is edited by picking from the category list rather than typing. */
  useCategoryPicker(index: number): boolean {
    return this.isCategoryField(index)
      && !this.customValueAt(index)
      && this.categoryValues().length > 0;
  }

  /** Escape hatch (B): flip a category condition between the picker and free text, explicitly. */
  toggleCustomValue(index: number): void {
    const ctrl = this.conditionsArray.at(index).get('customValue');
    ctrl?.setValue(!ctrl.value);
  }

  /**
   * A category condition whose value is not among the tenant's categories — it can never match, exactly
   * the silent typo this WI exists to surface. Only judged once the list has loaded and is non-empty.
   */
  categoryValueUnknown(index: number): boolean {
    if (!this.isCategoryField(index) || this.categoryValues().length === 0) return false;
    const known = new Set(this.categoryValues().map(c => c.toLowerCase()));
    if (this.usesSet(index)) {
      const set = this._parseSet(this.valueSetAt(index));
      return set.length > 0 && set.some(s => !known.has(s.toLowerCase()));
    }
    const raw = this.valueRawAt(index).trim();
    return raw.length > 0 && !known.has(raw.toLowerCase());
  }

  /** No categories exist yet (new/un-synced tenant): the admin still writes the rule via free text. */
  categoryListEmpty(index: number): boolean {
    return this.isCategoryField(index) && this.categoryValuesLoaded() && this.categoryValues().length === 0;
  }

  /**
   * Once the category list is known, force free-text mode for any stored condition whose value does not
   * match — so the value stays VISIBLE (never hidden behind an empty picker) alongside its warning, and
   * is never rewritten. Safe to run whenever either the list or the rule finishes loading.
   */
  private _reconcileCategoryModes(): void {
    if (this.categoryValues().length === 0) return;
    this.conditionsArray.controls.forEach((_, i) => {
      if (this.isCategoryField(i) && this.categoryValueUnknown(i)) {
        this.conditionsArray.at(i).get('customValue')?.setValue(true, { emitEvent: false });
      }
    });
  }

  /** Keeps valueType aligned with the picked field, so numeric/date comparisons use the right
   *  evaluator. Previously every condition was saved as String, which is why ordering operators
   *  never matched. */
  onFieldChange(index: number): void {
    const group = this.conditionsArray.at(index);
    const definition = this._definitionAt(index);
    if (!definition) return;

    group.get('valueType')?.setValue(
      ConditionValueType[definition.valueType as keyof typeof ConditionValueType] as number
    );

    // Drop an operator the new field does not support rather than submitting a dead filter.
    const allowed = definition.operators.map(
      op => ConditionOperator[op.operator as keyof typeof ConditionOperator] as number
    );
    if (!allowed.includes(Number(group.get('operator')?.value))) {
      group.get('operator')?.setValue(allowed[0]);
    }

    // Switching TO category starts the value fresh in picker mode: a leftover value from the previous
    // field (e.g. a product SKU) would otherwise be an unknown category and silently never match.
    if (this.fieldValueAt(index).toLowerCase() === this.CATEGORY_FIELD) {
      group.get('valueRaw')?.setValue('');
      group.get('valueSet')?.setValue('');
      group.get('customValue')?.setValue(false);
    }
  }

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(120)]],
    // Sort order is auto-assigned (max existing + 1) and presentation-only, so the field
    // is locked — users can't type arbitrary values. getRawValue() still submits it, and
    // setValue/patchValue below update disabled controls fine.
    sortOrder: [{ value: 1, disabled: true }, [Validators.required, Validators.min(1)]],
    measurement: this.fb.nonNullable.group({
      type: [MeasurementType.Revenue as number, Validators.required],
      sourceField: ['amount', Validators.required],
      aggregation: [MeasurementAggregation.Sum as number, Validators.required],
    }),
    rateTable: this.fb.nonNullable.group({
      type: [RateTableType.Flat as number, Validators.required],
      flatRate: [0.05],
      tiers: this.fb.array<FormGroup>([]),
      attainmentTiers: this.fb.array<FormGroup>([]),
      splitAtQuota: [false],
    }),
    hasTrigger: [false],
    trigger: this.fb.nonNullable.group({
      logicalOperator: [LogicalOperator.And as number],
      conditions: this.fb.array<FormGroup>([]),
    }),
    hasModifier: [false],
    modifier: this.fb.nonNullable.group({
      name: [''],
      type: [ModifierType.Accelerator as number],
      factor: [1.5],
    }),
    hasCap: [false],
    cap: this.fb.nonNullable.group({
      amount: [0, Validators.min(0)],
      scope: [CapScope.PerTransaction as number],
    }),
    hasFloor: [false],
    floor: this.fb.nonNullable.group({
      amount: [0, Validators.min(0)],
    }),
  });

  readonly formValue = toSignal(this.form.valueChanges, { initialValue: this.form.value });

  readonly rateTableType = computed(() => {
    const v = this.formValue();
    return Number(v.rateTable?.type ?? RateTableType.Flat) as RateTableType;
  });

  /**
   * i18n key for the always-visible help text under the Table Type selector — replaces the old hover
   * tooltip that ambiguously described Tiered. Each type gets an explanation of ITS calculation, so the
   * user sees the right one for what they picked (Tiered = progressive per-amount, Attainment = cumulative
   * vs quota).
   */
  readonly rateTableHintKey = computed(() => {
    switch (this.rateTableType()) {
      case RateTableType.Tiered: return 'PLANS.RATE_TABLE_HINT_TIERED';
      case RateTableType.AttainmentBased: return 'PLANS.RATE_TABLE_HINT_ATTAINMENT';
      default: return 'PLANS.RATE_TABLE_HINT_FLAT';
    }
  });

  readonly hasTrigger = computed(() => !!this.formValue()?.hasTrigger);
  readonly hasModifier = computed(() => !!this.formValue()?.hasModifier);
  readonly hasCap = computed(() => !!this.formValue()?.hasCap);
  readonly hasFloor = computed(() => !!this.formValue()?.hasFloor);

  /**
   * A floor ABOVE the cap makes the cap dead. The engine applies modifier → cap → floor
   * (`CreditAllocationService`), so the floor runs LAST and lifts the commission back over the
   * ceiling that was just applied: with cap 200 and floor 500, every matching transaction pays 500
   * and the cap never changes an outcome.
   *
   * A warning, not a validation error: the combination is contradictory rather than impossible, and
   * the domain accepts it. Blocking the save would be inventing a rule the backend does not have.
   */
  readonly floorExceedsCap = computed(() => {
    const v = this.formValue();
    if (!v?.hasCap || !v?.hasFloor) return false;
    const cap = Number(v.cap?.amount ?? 0);
    const floor = Number(v.floor?.amount ?? 0);
    return cap > 0 && floor > cap;
  });

  get tiersArray(): FormArray {
    return this.form.get('rateTable.tiers') as FormArray;
  }

  get attainmentTiersArray(): FormArray {
    return this.form.get('rateTable.attainmentTiers') as FormArray;
  }

  get conditionsArray(): FormArray {
    return this.form.get('trigger.conditions') as FormArray;
  }

  ngOnInit(): void {
    // The field catalog drives the picker, the operator lists and the value-type. Without it the
    // form has nothing valid to offer, so failures leave the list empty rather than falling back to
    // a hardcoded copy that could drift from the engine.
    this.plansApi.getTriggerFields().subscribe({
      next: fields => this.triggerFields.set(fields),
      error: () => this.triggerFields.set([]),
    });

    // The category picker's choices. Arrives independently of the rule load, so reconcile once it lands
    // (the rule may already be on screen). A failure leaves the list empty → free-text fallback, so the
    // admin is never blocked.
    this.plansApi.getCategoryValues().subscribe({
      next: values => {
        this.categoryValues.set(values);
        this.categoryValuesLoaded.set(true);
        this._reconcileCategoryModes();
      },
      error: () => this.categoryValuesLoaded.set(true),
    });

    // When the user switches to Units mode, force rate table to Flat (only supported combination).
    // Also ensure hidden fields stay at their defaults.
    this.form.controls.measurement.controls.type.valueChanges.pipe(
      distinctUntilChanged(),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(type => {
      if (Number(type) === MeasurementType.Units) {
        this.form.controls.rateTable.controls.type.setValue(RateTableType.Flat as number, { emitEvent: false });
      }
      // sourceField and aggregation are hidden; keep at defaults regardless of type.
      this.form.controls.measurement.controls.sourceField.setValue('amount', { emitEvent: false });
      this.form.controls.measurement.controls.aggregation.setValue(MeasurementAggregation.Sum as number, { emitEvent: false });
    });

    const loadPromise = (!this.store.selectedPlan() || this.store.selectedPlan()?.id !== this.planId)
      ? this.store.loadPlan(this.planId)
      : Promise.resolve();

    loadPromise.then(() => {
      if (this.isEdit) {
        this._loadExistingRule();  // populate signals while form is still enabled
      } else {
        // Default the sort order to max(existing) + 1 so new rules don't all land on #1.
        // Sort order is presentation-only (it does not affect payout amounts), so no
        // uniqueness validation is needed — this just avoids silent collisions by default.
        const existingRules = this.store.selectedPlan()?.rules ?? [];
        const nextSortOrder = existingRules.length > 0
          ? Math.max(...existingRules.map((r) => r.sortOrder)) + 1
          : 1;
        this.form.controls.sortOrder.setValue(nextSortOrder, { emitEvent: false });
        this._addTier();
      }

      const perms = getPlanPermissions(this.store.selectedPlan()?.status);
      if (!perms.canEditRule) {
        this.form.disable({ emitEvent: false });  // disable after load, no extra emission
        this.readOnly.set(true);
      }
    });
  }

  /**
   * The API uses JsonStringEnumConverter, so enum values arrive as string names
   * (e.g. "Revenue", "Flat"). The form options use numeric values (0, 1, 2…).
   * This helper coerces either representation to the numeric value so that
   * WsSelect's `selectedOption` comparison (===) finds the correct option.
   */
  private _enumToNumber<T extends Record<string, unknown>>(enumObj: T, value: unknown): number {
    if (typeof value === 'number') return value;
    if (typeof value === 'string') {
      const n = enumObj[value];
      return typeof n === 'number' ? n : 0;
    }
    return 0;
  }

  private _loadExistingRule(): void {
    const plan = this.store.selectedPlan();
    const rule = plan?.rules.find((r) => r.id === this.ruleId);
    if (!rule) return;

    const rateTableTypeNum = this._enumToNumber(RateTableType, rule.rateTable.type);

    this.form.patchValue({
      name: rule.name,
      sortOrder: rule.sortOrder,
      measurement: {
        type: this._enumToNumber(MeasurementType, rule.measurement.type),
        sourceField: rule.measurement.sourceField,
        aggregation: this._enumToNumber(MeasurementAggregation, rule.measurement.aggregation),
      },
      rateTable: {
        type: rateTableTypeNum,
        flatRate: rule.rateTable.flatRate ?? 0.05,
        splitAtQuota: rule.rateTable.splitAtQuota ?? false,
      },
      hasTrigger: !!rule.trigger,
      hasModifier: !!rule.modifier,
      hasCap: !!rule.cap,
      hasFloor: !!rule.floor,
    });

    if (rule.trigger) {
      this.form.patchValue({
        trigger: { logicalOperator: this._enumToNumber(LogicalOperator, rule.trigger.logicalOperator) },
      });
      rule.trigger.conditions.forEach((c) => {
        this.conditionsArray.push(
          this.fb.nonNullable.group({
            field: [c.field, Validators.required],
            operator: [this._enumToNumber(ConditionOperator, c.operator)],
            valueType: [this._enumToNumber(ConditionValueType, c.value.type)],
            valueRaw: [c.value.raw],
            // Stored as a list; edited as comma-separated text.
            valueSet: [(c.value.set ?? []).join(', ')],
            customValue: [false],
          })
        );
        this._wireFieldChange(this.conditionsArray.length - 1);
      });
      // If the category list already loaded, drop unknown-valued category conditions to free text so
      // their value stays visible next to the warning (and is never rewritten).
      this._reconcileCategoryModes();
    }

    if (rule.modifier) {
      this.form.patchValue({
        modifier: {
          name: rule.modifier.name,
          type: this._enumToNumber(ModifierType, rule.modifier.type),
          factor: rule.modifier.factor,
        },
      });
    }

    if (rule.cap) {
      this.form.patchValue({ cap: { amount: rule.cap.amount.amount, scope: this._enumToNumber(CapScope, rule.cap.scope) } });
    }

    if (rule.floor) {
      this.form.patchValue({ floor: { amount: rule.floor.amount.amount } });
    }

    if (rateTableTypeNum === RateTableType.Tiered && rule.rateTable.tiers) {
      rule.rateTable.tiers.forEach((t) => {
        this.tiersArray.push(
          this.fb.nonNullable.group({ from: [t.from], to: [t.to], rate: [t.rate] })
        );
      });
    } else if (rateTableTypeNum === RateTableType.AttainmentBased && rule.rateTable.attainmentTiers) {
      rule.rateTable.attainmentTiers.forEach((t) => {
        this.attainmentTiersArray.push(
          this.fb.nonNullable.group({ attainmentFrom: [t.attainmentFrom], attainmentTo: [t.attainmentTo], rate: [t.rate] })
        );
      });
    }
  }

  addTier(): void { this._addTier(); }
  removeTier(i: number): void { this.tiersArray.removeAt(i); }

  addAttainmentTier(): void { this._addAttainmentTier(); }
  removeAttainmentTier(i: number): void { this.attainmentTiersArray.removeAt(i); }

  addCondition(): void {
    this.conditionsArray.push(
      this.fb.nonNullable.group({
        field: ['', Validators.required],
        operator: [ConditionOperator.Equal],
        valueType: [ConditionValueType.String],
        valueRaw: [''],
        valueSet: [''],
        // Category only: false = pick from the list, true = the explicit "use another value" escape hatch.
        customValue: [false],
      })
    );
    this._wireFieldChange(this.conditionsArray.length - 1);
  }

  /** Re-aligns valueType and the operator list whenever the picked field changes. */
  private _wireFieldChange(index: number): void {
    this.conditionsArray.at(index).get('field')?.valueChanges
      .pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.onFieldChange(index));
  }

  removeCondition(i: number): void { this.conditionsArray.removeAt(i); }

  private _addTier(): void {
    const last = this.tiersArray.controls.at(-1)?.value;
    const from = last ? (last.to ?? 0) : 0;
    this.tiersArray.push(
      this.fb.nonNullable.group({ from: [from], to: [null as number | null], rate: [0.05] })
    );
  }

  private _addAttainmentTier(): void {
    const last = this.attainmentTiersArray.controls.at(-1)?.value;
    const from = last ? (last.attainmentTo ?? 0) : 0;
    this.attainmentTiersArray.push(
      this.fb.nonNullable.group({ attainmentFrom: [from], attainmentTo: [null as number | null], rate: [0.05] })
    );
  }

  asFormGroup(ctrl: AbstractControl): FormGroup {
    return ctrl as FormGroup;
  }

  // Form values can be numeric (on create) or string names (when patched from API).
  // These helpers normalise to the enum name string so the i18n key is always correct.
  measurementTypeKey(value: unknown): string {
    if (value == null || value === '') return '';
    const asNum = Number(value);
    const name = !isNaN(asNum) ? (MeasurementType[asNum] ?? String(value)) : String(value);
    return `PLANS.MEASUREMENT_${name.toUpperCase()}`;
  }

  measurementAggregationKey(value: unknown): string {
    if (value == null || value === '') return '';
    const asNum = Number(value);
    const name = !isNaN(asNum) ? (MeasurementAggregation[asNum] ?? String(value)) : String(value);
    return `PLANS.AGGREGATION_${name.toUpperCase()}`;
  }

  async onSubmit(): Promise<void> {
    if (this.readOnly()) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      // Never fail silently: tell the user why nothing happened on Save.
      this.toast.show('PLANS.TOAST_RULE_INVALID', 'error');
      return;
    }
    this.saving.set(true);
    try {
      const v = this.form.getRawValue();
      const currency = this.store.selectedPlan()?.currency ?? 'USD';

      const request: AddRuleRequest = {
        planId: this.planId,
        name: v.name,
        sortOrder: v.sortOrder,
        measurement: {
          _schema: 1,
          type: Number(v.measurement.type),
          sourceField: v.measurement.sourceField,
          aggregation: Number(v.measurement.aggregation),
        },
        rateTable: this._buildRateTable(v),
        trigger: v.hasTrigger ? this._buildTrigger(v) : null,
        modifier: v.hasModifier ? this._buildModifier(v) : null,
        // Send the enum NAME (not the number) so the backend deserializes by name — this makes
        // any frontend/backend numeric misalignment of CapScope harmless. See WI CapScope.
        cap: v.hasCap ? { _schema: 1, amount: { amount: v.cap.amount, currency }, scope: CapScope[Number(v.cap.scope)] as unknown as CapScope } : null,
        floor: v.hasFloor ? { _schema: 1, amount: { amount: v.floor.amount, currency } } : null,
      };

      if (this.isEdit) {
        await this.store.updateRule(this.planId, this.ruleId!, { ...request, ruleId: this.ruleId! } as UpdateRuleRequest);
        this.toast.show('PLANS.TOAST_RULE_UPDATED', 'success');
      } else {
        await this.store.addRule(this.planId, request);
        this.toast.show('PLANS.TOAST_RULE_ADDED', 'success');
      }
      this.router.navigate(['/plans', this.planId]);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.saving.set(false);
    }
  }

  private _buildRateTable(v: ReturnType<typeof this.form.getRawValue>) {
    const type = Number(v.rateTable.type) as RateTableType;
    return {
      _schema: 1 as const,
      type,
      flatRate: type === RateTableType.Flat ? v.rateTable.flatRate : null,
      tiers: type === RateTableType.Tiered
        ? v.rateTable.tiers.map((t) => ({ from: t['from'], to: t['to'], rate: t['rate'] }))
        : null,
      attainmentTiers: type === RateTableType.AttainmentBased
        ? v.rateTable.attainmentTiers.map((t) => ({ attainmentFrom: t['attainmentFrom'], attainmentTo: t['attainmentTo'], rate: t['rate'] }))
        : null,
      splitAtQuota: type === RateTableType.AttainmentBased ? (v.rateTable.splitAtQuota ?? false) : false,
    };
  }

  private _buildTrigger(v: ReturnType<typeof this.form.getRawValue>) {
    return {
      _schema: 1 as const,
      logicalOperator: v.trigger.logicalOperator,
      conditions: v.trigger.conditions.map((c) => {
        const operator = Number(c['operator']);
        // In/NotIn are read from `set`; everything else from `raw`. The form used to send set: null
        // unconditionally, which made those two operators unreachable however they were configured.
        const isSetOperator =
          operator === ConditionOperator.In || operator === ConditionOperator.NotIn;
        const set = isSetOperator
          ? String(c['valueSet'] ?? '').split(',').map((s) => s.trim()).filter((s) => s.length > 0)
          : null;
        return {
          field: c['field'],
          operator,
          value: {
            type: Number(c['valueType']),
            raw: isSetOperator ? '' : c['valueRaw'],
            set,
          },
        };
      }),
    };
  }

  private _buildModifier(v: ReturnType<typeof this.form.getRawValue>) {
    return {
      _schema: 1 as const,
      id: this.isEdit ? (this.store.selectedPlan()?.rules.find((r) => r.id === this.ruleId)?.modifier?.id ?? crypto.randomUUID()) : crypto.randomUUID(),
      name: v.modifier.name,
      type: Number(v.modifier.type),
      factor: v.modifier.factor,
      trigger: null,
    };
  }
}
