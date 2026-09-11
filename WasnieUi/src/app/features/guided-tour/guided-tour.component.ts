import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom, throwError } from 'rxjs';
import {
  SelectOption,
  WsButtonComponent,
  WsCardComponent,
  WsConfirmationModalComponent,
  WsDatePickerComponent,
  WsGuideStep,
  WsGuideStepperComponent,
  WsInputComponent,
  WsModalComponent,
  WsPageLayoutComponent,
  WsSelectComponent,
} from '../../shared/ui';
import { AppShellComponent } from '../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../shared/components/icon/icon.component';
import { HasPermissionPipe } from '../../shared/pipes/has-permission.pipe';
import { CurrencyFormatPipe } from '../../shared/pipes/currency-format.pipe';
import { formatRate } from '../../shared/utils/rate-format';
import { createRuleDefinitionForm } from '../plans/rule-form/rule-definition-form';
import { RuleDefinitionFieldsComponent } from '../plans/rule-form/rule-definition-fields.component';
import { RulePreviewComponent } from '../plans/rule-form/rule-preview.component';
import { GuidedStepId, SandboxExperiment, SandboxRule } from './models/guided-tour.model';
import { SandboxApiService } from './services/sandbox.api.service';
import { GUIDED_STEPS, GuidedTourStore } from './state/guided-tour.store';

/**
 * Una fecha en el formato que esperan los comandos.
 *
 * ★ SE ARMA CON LAS PARTES LOCALES, NO CON `toISOString()`. Esa función convierte a UTC primero: una
 * fecha creada a medianoche local en un huso por delante de Greenwich retrocede un día, y el período
 * del plan aparecía empezando el 31 de diciembre del año anterior. Se ve en pantalla, no compilando.
 */
function isoDate(date: Date): string {
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  const day = `${date.getDate()}`.padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

/** Las mediciones de la cuota que ofrece la pantalla real de cuotas, con los valores de SU enum. */
const QUOTA_REVENUE = 0;
const QUOTA_UNITS = 2;

@Component({
  selector: 'app-guided-tour',
  standalone: true,
  imports: [
    AppShellComponent, DatePipe, IconComponent, ReactiveFormsModule, TranslatePipe, WsPageLayoutComponent,
    WsCardComponent,
    WsButtonComponent, WsInputComponent, WsSelectComponent, WsDatePickerComponent, WsModalComponent,
    WsGuideStepperComponent, WsConfirmationModalComponent, RouterLink, HasPermissionPipe, CurrencyFormatPipe,
    RuleDefinitionFieldsComponent, RulePreviewComponent,
  ],
  templateUrl: './guided-tour.component.html',
  styleUrl: './guided-tour.component.scss',
})
export class GuidedTourComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(SandboxApiService);
  private readonly translate = inject(TranslateService);
  readonly store = inject(GuidedTourStore);

  readonly resetting = signal(false);

  private readonly today = new Date();
  private readonly yearStart = isoDate(new Date(this.today.getFullYear(), 0, 1));
  private readonly yearEnd = isoDate(new Date(this.today.getFullYear(), 11, 31));

  /**
   * Los datos del recorrido, con lo esencial de cada paso.
   *
   * ★★ VIENE RELLENO, Y ESO ES PARTE DEL DISEÑO. Lo que se practica aquí es entender el ciclo y probar
   * configuraciones, no teclear nombres. Todo es editable: quien venga a experimentar con sus propios
   * números — que es para lo que existe este sandbox — cambia lo que quiera.
   *
   * ★ LA REGLA NO ESTÁ AQUÍ. Vive en {@link ruleDef}, que es el mismo formulario de la pantalla real de
   * reglas; ver la nota de ese campo.
   */
  readonly form = this.fb.nonNullable.group({
    // Plan
    planName: ['Demo plan', Validators.required],
    planDescription: [''],
    currency: ['EUR', Validators.required],
    periodStart: [this.yearStart, Validators.required],
    periodEnd: [this.yearEnd, Validators.required],

    // Payee
    payeeName: ['Alex Demo', Validators.required],
    employeeCode: ['DEMO-001', Validators.required],
    payeeEmail: [''],
    payeeRole: [''],

    // Cuota
    quotaAmount: [100000, [Validators.required, Validators.min(1)]],
    quotaMeasurement: [QUOTA_REVENUE, Validators.required],

    // Venta
    txReference: ['DEMO-0001', Validators.required],
    txAmount: [10000, [Validators.required, Validators.min(1)]],
    txDate: [isoDate(this.today), Validators.required],
    txDescription: [''],
  });

  /**
   * La regla, con TODAS las opciones de la pantalla real — porque ES la pantalla real.
   *
   * ★★ EL MISMO FORMULARIO, NO UNA COPIA. La primera versión del recorrido tenía una regla escrita a
   * mano con cinco campos, y en una semana ya contaba otra historia que la pantalla de Planes: el tope
   * y el mínimo viajaban como `{ type, value }` y los tramos por cumplimiento como `{ from, to }`,
   * formas que el servidor no lee — llegaban a cero y nada fallaba. Ahora el recorrido y Planes
   * comparten `RuleDefinitionForm`: mismos campos (tabla plana, por tramos o por cumplimiento,
   * disparador, modificador, tope y mínimo), mismos avisos, mismo constructor del cuerpo y el mismo
   * simulador. Cuando la regla gane una opción en Planes, aparece aquí sin tocar esta pantalla.
   *
   * Lo único propio del recorrido es a qué plan pertenece la regla y adónde se manda la simulación:
   * al plan de práctica, por la ruta del sandbox.
   */
  readonly ruleDef = createRuleDefinitionForm({
    planId: () => this.store.status().plan?.id ?? '',
    currency: computed(() => this.store.status().plan?.currency ?? this.form.controls.currency.value),
    readOnly: signal(false),
    simulate: (request) => {
      const planId = this.store.status().plan?.id;
      // Sin plan no hay moneda ni contexto que simular; se dice lo que falta en vez de inventarlo.
      return planId
        ? this.api.simulateRule(planId, request)
        : throwError(() => new HttpErrorResponse({ status: 422, error: { message: 'GUIDED.BLOCKED.NEEDS_PLAN' } }));
    },
  });

  readonly currencyOptions: SelectOption[] = [
    { value: 'EUR', label: 'EUR' },
    { value: 'USD', label: 'USD' },
    { value: 'PLN', label: 'PLN' },
  ];

  /**
   * ★ LAS DOS QUE OFRECE LA PANTALLA REAL DE CUOTAS, NO TRES. La lista anterior añadía «Margen», que el
   * formulario real de cuotas no ofrece: el recorrido enseñaba una opción del producto que no existe.
   * Los valores son los del enum de CUOTAS (Units = 2), que no es el de la medición de una regla.
   */
  readonly measurementOptions: SelectOption[] = [
    { value: QUOTA_REVENUE, label: 'GUIDED.MEASUREMENT.REVENUE' },
    { value: QUOTA_UNITS, label: 'GUIDED.MEASUREMENT.UNITS' },
  ];

  readonly currentStep = computed(() => this.store.currentStep());

  /**
   * Los pasos, ya traducidos: la primitiva recibe texto, no claves.
   *
   * ★★ EL MOTIVO DEL BLOQUEO NO VIAJA AL STEPPER, Y ES DELIBERADO. El mismo hecho se estaba contando
   * dos veces en la misma pantalla — una frase bajo el formulario y otra distinta en la lista — y dos
   * mensajes para un solo bloqueo hacen dudar de los dos. El stepper muestra el ESTADO (candado,
   * atenuado); el motivo se explica una sola vez, junto al botón que no se puede pulsar. La primitiva
   * sigue admitiéndolo para quien lo necesite en otra pantalla.
   */
  readonly steps = computed<WsGuideStep[]>(() =>
    this.store.steps().map(step => ({
      ...step,
      title: this.translate.instant(step.title),
      description: step.description ? this.translate.instant(step.description) : undefined,
      blockedReason: undefined,
    }))
  );

  readonly blockedReason = computed(() => this.store.blockedReasonKey(this.currentStep()));
  readonly isDone = computed(() => this.store.isDone(this.currentStep()));
  readonly finished = computed(() => this.store.completed() === GUIDED_STEPS.length);

  /** La foto del experimento que se está revisando, o `null` si se está en el recorrido en curso. */
  readonly reviewed = computed(() => {
    this.store.openExperiment();
    return this.store.reviewedStatus();
  });

  ngOnInit(): void {
    void this.store.refresh();
    void this.store.loadExperiments();

    this.ruleDef.loadCatalogs();
    this.ruleDef.prepareForCreate(1);
    // El nombre por defecto, en el idioma del usuario. Se espera a la traducción: `instant` antes de
    // que cargue devolvería la clave y la regla se llamaría «GUIDED.STEP.RULE.NAME_VALUE».
    void firstValueFrom(this.translate.get('GUIDED.STEP.RULE.NAME_VALUE')).then(name => {
      if (!this.ruleDef.form.controls.name.value) this.ruleDef.form.controls.name.setValue(name);
    });
  }

  /** Los campos que este paso necesita. La zona de acción no muestra ni uno de más. */
  shows(field: string): boolean {
    const fields: Record<GuidedStepId, string[]> = {
      plan: ['planName', 'planDescription', 'currency', 'periodStart', 'periodEnd'],
      // La regla no usa esta lista: pinta el formulario de reglas completo (ver la plantilla).
      rule: [],
      payee: ['payeeName', 'employeeCode', 'payeeEmail', 'payeeRole'],
      quota: ['quotaAmount', 'quotaMeasurement'],
      assignment: ['periodStart', 'periodEnd'],
      transaction: ['txReference', 'txAmount', 'txDate', 'txDescription'],
      calculate: [],
      payrun: ['periodStart', 'periodEnd'],
      approve: [],
      pay: [],
    };
    return fields[this.currentStep()].includes(field);
  }

  /** Si al paso abierto le falta algo por rellenar. La regla se valida con su propio formulario. */
  stepInvalid(): boolean {
    return this.currentStep() === 'rule' ? this.ruleDef.form.invalid : this.form.invalid;
  }

  select(id: string): void {
    this.store.open(id);
  }

  /**
   * Ejecuta el paso abierto llamando al comando real del producto.
   *
   * ★ CADA RAMA ES UNA LLAMADA Y NADA MÁS. Aquí no se decide qué es una comisión ni cuándo se puede
   * pagar un pay run: eso lo decide el motor, igual que en las pantallas reales. Si mañana el motor
   * cambia una regla, este recorrido enseña la nueva sin tocar una línea.
   */
  async execute(): Promise<void> {
    const step = this.currentStep();
    const v = this.form.getRawValue();
    const s = this.store.status();

    await this.store.run(() => {
      switch (step) {
        case 'plan':
          return firstValueFrom(this.api.createPlan({
            name: v.planName,
            description: v.planDescription || this.translate.instant('GUIDED.STEP.PLAN.DESCRIPTION_VALUE'),
            effectiveStart: v.periodStart,
            effectiveEnd: v.periodEnd,
            currency: v.currency,
          }));

        case 'rule':
          // Numerada detrás de las que el plan de práctica ya tenga, igual que en Planes.
          this.ruleDef.form.controls.sortOrder.setValue((s.plan?.ruleCount ?? 0) + 1);
          // ★ EL CUERPO LO ARMA EL MISMO CONSTRUCTOR QUE GUARDA EN PLANES. Nada se traduce aquí.
          return firstValueFrom(this.api.addRule(this.ruleDef.buildDefinition()));

        case 'payee':
          return firstValueFrom(this.api.createPayee({
            fullName: v.payeeName,
            employeeCode: v.employeeCode,
            email: v.payeeEmail || null,
            hireDate: v.periodStart,
            role: v.payeeRole || null,
          }));

        case 'quota':
          return firstValueFrom(this.api.createQuota({
            payeeId: s.payee!.id,
            planId: s.plan!.id,
            measurementType: Number(v.quotaMeasurement),
            amount: v.quotaAmount,
            currency: v.currency,
            periodStart: v.periodStart,
            periodEnd: v.periodEnd,
          }));

        case 'assignment':
          return firstValueFrom(this.api.assign({
            planId: s.plan!.id,
            payeeId: s.payee!.id,
            effectiveStart: v.periodStart,
            effectiveEnd: v.periodEnd,
          }));

        case 'transaction':
          return firstValueFrom(this.api.ingestTransaction({
            referenceNumber: v.txReference,
            payeeId: s.payee!.id,
            amount: v.txAmount,
            currency: v.currency,
            transactionDate: v.txDate,
            description: v.txDescription || null,
            // El recorrido calcula en su propio paso, para que el usuario vea las dos mitades.
            processImmediately: false,
          }));

        case 'calculate':
          return firstValueFrom(this.api.process());

        case 'payrun':
          return firstValueFrom(this.api.calculatePayRun({
            periodStart: v.periodStart,
            periodEnd: v.periodEnd,
          }));

        case 'approve':
          return firstValueFrom(this.api.approvePayRun({ payRunId: s.payRun!.id }));

        case 'pay':
          return firstValueFrom(this.api.markPaid({ payRunId: s.payRun!.id }));
      }
    }, step);
  }

  // ── El resumen de la regla creada ─────────────────────────────────────────────────

  /**
   * La clave que nombra el tipo de tabla.
   *
   * ★ LISTA BLANCA, NUNCA `PLANS.RATE_TABLE_${tipo}`. El servidor manda el nombre del enum; antes se
   * pintaba tal cual («AttainmentBased») y, concatenado, un tipo que esta versión no conozca imprimiría
   * un identificador interno. Lo desconocido cae a una etiqueta genérica.
   */
  tableTypeKey(type: string): string {
    switch (type) {
      case 'Flat': return 'PLANS.RATE_TABLE_FLAT';
      case 'Tiered': return 'PLANS.RATE_TABLE_TIERED';
      case 'AttainmentBased': return 'PLANS.RATE_TABLE_ATTAINMENTBASED';
      default: return 'GUIDED.RESULT.RULE_TYPE_UNKNOWN';
    }
  }

  /**
   * La tasa plana escrita según lo que significa: 0.05 es «5 %» sobre importes, y 5 es «5,00 € por
   * unidad» midiendo unidades. Pasa por `rate-format.ts`, que es el único sitio del producto que decide
   * eso; `null` cuando la tabla no es plana y no hay una tasa única que enseñar.
   */
  ruleRate(rule: SandboxRule, currency: string): string | null {
    if (rule.rate === null || rule.rate === undefined) return null;
    const base = rule.measurementType === 'Units' ? 'TransactionQuantity' : 'TransactionAmount';
    return formatRate(
      rule.rate, base, currency,
      this.translate.currentLang || 'en',
      this.translate.instant('GUIDED.RESULT.PER_UNIT'),
    );
  }

  // ── Historial de experimentos ─────────────────────────────────────────────────────

  /**
   * El nombre del experimento se pide en un modal del sistema.
   *
   * ★ NO CON `window.prompt`. El diálogo del navegador es justo lo que el design system prohíbe
   * (§5.4): no se puede traducir su botón, no respeta el tema y bloquea la página entera.
   *
   * ★ EL NOMBRE SE PIDE, NO SE INVENTA. «Experimento 3» no le dice nada a nadie dentro de un mes; el
   * usuario sabe qué estaba probando y es quien puede nombrarlo.
   */
  readonly nameModalOpen = signal(false);
  readonly nameControl = new FormControl('', { nonNullable: true, validators: [Validators.required] });
  readonly savingName = signal(false);

  /** El experimento que se está renombrando, o `null` si se guarda uno nuevo. */
  readonly renamingId = signal<string | null>(null);

  saveExperiment(): void {
    this.renamingId.set(null);
    this.nameControl.setValue(
      this.store.status().plan?.name ?? this.translate.instant('GUIDED.EXPERIMENTS.DEFAULT_NAME'),
    );
    this.nameModalOpen.set(true);
  }

  renameExperiment(experiment: SandboxExperiment): void {
    this.renamingId.set(experiment.id);
    this.nameControl.setValue(experiment.name);
    this.nameModalOpen.set(true);
  }

  async confirmName(): Promise<void> {
    const name = this.nameControl.value.trim();
    if (!name) {
      this.nameControl.markAsTouched();
      return;
    }

    this.savingName.set(true);
    try {
      await this.store.saveExperiment(name, this.renamingId());
      this.nameModalOpen.set(false);
    } finally {
      this.savingName.set(false);
    }
  }

  async deleteExperiment(experiment: SandboxExperiment): Promise<void> {
    await this.store.deleteExperiment(experiment.id);
  }

  // ── Promoción a plan real ─────────────────────────────────────────────────────────

  readonly promoteOpen = signal(false);

  /**
   * Promueve la configuración probada a un plan real, previa confirmación.
   *
   * ★★ LA CONFIRMACIÓN NO ES CORTESÍA. Todo lo demás de esta pantalla es reversible de un botón: se
   * resetea y no queda nada. Ésta es la única acción del recorrido que sale del sandbox y crea algo en
   * el lado del dinero, y el usuario tiene que saber que a partir de aquí lo que toque ya no es de
   * mentira. El modal lo dice antes, no después.
   */
  async promote(): Promise<void> {
    await this.store.promote();
    this.promoteOpen.set(false);
  }

  async reset(): Promise<void> {
    this.resetting.set(true);
    try {
      await this.store.reset();
    } finally {
      this.resetting.set(false);
    }
  }
}
