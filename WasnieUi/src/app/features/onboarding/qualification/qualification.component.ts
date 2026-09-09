import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { isValidPhoneNumber, validatePhoneNumberLength, CountryCode } from 'libphonenumber-js/min';
import { TranslatePipe } from '@ngx-translate/core';
import { CurrentUserService } from '../../../core/auth/current-user.service';
import { OnboardingService } from '../services/onboarding.service';
import { SelectOption, WsButtonComponent, WsInputComponent, WsSelectComponent } from '../../../shared/ui';

interface EuCountry {
  value: string;
  label: string;
  prefix: string;
  localDigits: { min: number; max: number };
  /**
   * El ejemplo que se muestra bajo el campo.
   *
   * ★★ SIN ESPACIOS EN LA PARTE LOCAL, Y NO ES UN DESCUIDO. El campo borra todo lo que no sea dígito
   * mientras se escribe, así que un ejemplo como «+34 612 345 678» enseñaba un formato que el propio
   * campo se niega a conservar — y un usuario dedujo, con toda lógica, que el error se debía a que
   * faltaban esos espacios. La pantalla tiene que mostrar lo que el campo acepta.
   *
   * ★ Los espacios nunca importaron para validar: la librería los ignora. Lo que importa es cuántos
   * dígitos hay y cómo empiezan.
   */
  example: string;
}

const EU_COUNTRIES: EuCountry[] = [
  { value: 'AT', label: 'QUALIFY.COUNTRY_AT', prefix: '+43',  localDigits: { min: 4,  max: 13 }, example: '+43 6641234567'   },
  { value: 'BE', label: 'QUALIFY.COUNTRY_BE', prefix: '+32',  localDigits: { min: 8,  max: 9  }, example: '+32 470123456'  },
  { value: 'BG', label: 'QUALIFY.COUNTRY_BG', prefix: '+359', localDigits: { min: 8,  max: 9  }, example: '+359 871234567'  },
  { value: 'HR', label: 'QUALIFY.COUNTRY_HR', prefix: '+385', localDigits: { min: 8,  max: 9  }, example: '+385 912345678'  },
  { value: 'CY', label: 'QUALIFY.COUNTRY_CY', prefix: '+357', localDigits: { min: 8,  max: 8  }, example: '+357 96123456'    },
  { value: 'CZ', label: 'QUALIFY.COUNTRY_CZ', prefix: '+420', localDigits: { min: 9,  max: 9  }, example: '+420 601123456'  },
  { value: 'DK', label: 'QUALIFY.COUNTRY_DK', prefix: '+45',  localDigits: { min: 8,  max: 8  }, example: '+45 20123456'   },
  { value: 'EE', label: 'QUALIFY.COUNTRY_EE', prefix: '+372', localDigits: { min: 7,  max: 8  }, example: '+372 51234567'    },
  { value: 'FI', label: 'QUALIFY.COUNTRY_FI', prefix: '+358', localDigits: { min: 6,  max: 12 }, example: '+358 401234567'   },
  { value: 'FR', label: 'QUALIFY.COUNTRY_FR', prefix: '+33',  localDigits: { min: 9,  max: 9  }, example: '+33 612345678' },
  { value: 'DE', label: 'QUALIFY.COUNTRY_DE', prefix: '+49',  localDigits: { min: 10, max: 12 }, example: '+49 15123456789'  },
  { value: 'GR', label: 'QUALIFY.COUNTRY_GR', prefix: '+30',  localDigits: { min: 10, max: 10 }, example: '+30 6941234567'  },
  { value: 'HU', label: 'QUALIFY.COUNTRY_HU', prefix: '+36',  localDigits: { min: 8,  max: 9  }, example: '+36 201234567'   },
  { value: 'IE', label: 'QUALIFY.COUNTRY_IE', prefix: '+353', localDigits: { min: 7,  max: 9  }, example: '+353 851234567'  },
  { value: 'IT', label: 'QUALIFY.COUNTRY_IT', prefix: '+39',  localDigits: { min: 6,  max: 11 }, example: '+39 3331234567'  },
  { value: 'LV', label: 'QUALIFY.COUNTRY_LV', prefix: '+371', localDigits: { min: 8,  max: 8  }, example: '+371 20123456'    },
  { value: 'LT', label: 'QUALIFY.COUNTRY_LT', prefix: '+370', localDigits: { min: 8,  max: 8  }, example: '+370 61234567'    },
  { value: 'LU', label: 'QUALIFY.COUNTRY_LU', prefix: '+352', localDigits: { min: 6,  max: 9  }, example: '+352 621123456'  },
  { value: 'MT', label: 'QUALIFY.COUNTRY_MT', prefix: '+356', localDigits: { min: 8,  max: 8  }, example: '+356 99123456'    },
  { value: 'NL', label: 'QUALIFY.COUNTRY_NL', prefix: '+31',  localDigits: { min: 9,  max: 9  }, example: '+31 612345678'    },
  { value: 'PL', label: 'QUALIFY.COUNTRY_PL', prefix: '+48',  localDigits: { min: 9,  max: 9  }, example: '+48 512345678'   },
  { value: 'PT', label: 'QUALIFY.COUNTRY_PT', prefix: '+351', localDigits: { min: 9,  max: 9  }, example: '+351 912345678'  },
  { value: 'RO', label: 'QUALIFY.COUNTRY_RO', prefix: '+40',  localDigits: { min: 9,  max: 9  }, example: '+40 712345678'   },
  { value: 'SK', label: 'QUALIFY.COUNTRY_SK', prefix: '+421', localDigits: { min: 9,  max: 9  }, example: '+421 901123456'  },
  { value: 'SI', label: 'QUALIFY.COUNTRY_SI', prefix: '+386', localDigits: { min: 8,  max: 8  }, example: '+386 31234567'   },
  { value: 'ES', label: 'QUALIFY.COUNTRY_ES', prefix: '+34',  localDigits: { min: 9,  max: 9  }, example: '+34 612345678'   },
  { value: 'SE', label: 'QUALIFY.COUNTRY_SE', prefix: '+46',  localDigits: { min: 7,  max: 11 }, example: '+46 701234567'  },
];

@Component({
  selector: 'app-qualification',
  standalone: true,
  imports: [ReactiveFormsModule, TranslatePipe, WsButtonComponent, WsSelectComponent, WsInputComponent],
  templateUrl: './qualification.component.html',
  styleUrl: './qualification.component.scss',
})
export class QualificationComponent {
  private readonly fb = inject(FormBuilder);
  private readonly onboardingService = inject(OnboardingService);
  private readonly currentUser = inject(CurrentUserService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    country:          ['', Validators.required],
    phoneNumber:      ['', [Validators.required, (c: AbstractControl) => this.validatePhone(c)]],
    // Attribution is marketing, not compliance: optional so it never blocks qualification.
    // The backend column is nullable and unvalidated, so an empty string is accepted.
    howHeardAboutUs:  [''],
    salesVolumeRange: ['', Validators.required],
    currentSystem:    ['', Validators.required],
    legalAccepted:    [false, Validators.requiredTrue],
  });

  private readonly countryValue = toSignal(
    this.form.get('country')!.valueChanges,
    { initialValue: '' }
  );

  readonly selectedCountry = computed(() =>
    EU_COUNTRIES.find(c => c.value === this.countryValue()) ?? null
  );

  readonly phoneExample = computed(() => this.selectedCountry()?.example ?? '');

  // prefix length + 1 space + max local digits  e.g. "+34 " (4) + 9 = 13
  readonly phoneMaxLength = computed(() => {
    const c = this.selectedCountry();
    return c ? c.prefix.length + 1 + c.localDigits.max : null;
  });

  readonly countryOptions: SelectOption[] = EU_COUNTRIES.map(c => ({ value: c.value, label: c.label }));

  readonly howHeardOptions: SelectOption[] = [
    { value: 'web_search',   label: 'QUALIFY.HOW_WEB_SEARCH'   },
    { value: 'referral',     label: 'QUALIFY.HOW_REFERRAL'     },
    { value: 'social_media', label: 'QUALIFY.HOW_SOCIAL_MEDIA' },
    { value: 'community',    label: 'QUALIFY.HOW_COMMUNITY'    },
    { value: 'other',        label: 'QUALIFY.HOW_OTHER'        },
  ];

  readonly salesVolumeOptions: SelectOption[] = [
    { value: 'under_1m',  label: 'QUALIFY.VOL_UNDER_1M'  },
    { value: '1m_5m',     label: 'QUALIFY.VOL_1M_5M'     },
    { value: '5m_20m',    label: 'QUALIFY.VOL_5M_20M'    },
    { value: 'over_20m',  label: 'QUALIFY.VOL_OVER_20M'  },
  ];

  readonly currentSystemOptions: SelectOption[] = [
    { value: 'excel',        label: 'QUALIFY.SYS_EXCEL'        },
    { value: 'xactly',       label: 'QUALIFY.SYS_XACTLY'       },
    { value: 'spiff',        label: 'QUALIFY.SYS_SPIFF'        },
    { value: 'sap',          label: 'QUALIFY.SYS_SAP'          },
    { value: 'captivate_iq', label: 'QUALIFY.SYS_CAPTIVATE_IQ' },
    { value: 'other',        label: 'QUALIFY.SYS_OTHER'        },
    { value: 'none',         label: 'QUALIFY.SYS_NONE'         },
  ];

  constructor() {
    // Auto-fill country prefix when country changes
    this.form.get('country')!.valueChanges.pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(countryCode => {
      const country = EU_COUNTRIES.find(c => c.value === countryCode);
      if (!country) return;

      const current = this.form.get('phoneNumber')!.value;
      const oldCountry = EU_COUNTRIES.find(c => current.startsWith(c.prefix));
      // Preserve digits the user already typed when switching country
      const existingDigits = oldCountry
        ? current.slice(oldCountry.prefix.length).replace(/\D/g, '').slice(0, country.localDigits.max)
        : '';

      this.form.get('phoneNumber')!.setValue(
        existingDigits ? `${country.prefix} ${existingDigits}` : `${country.prefix} `,
        { emitEvent: false }
      );
      this.form.get('phoneNumber')!.updateValueAndValidity();
    });

    // Real-time phone sanitization: digits only, enforce max per country
    this.form.get('phoneNumber')!.valueChanges.pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(raw => {
      const countryCode = this.form.get('country')!.value as string;
      const country = EU_COUNTRIES.find(c => c.value === countryCode);
      if (!country) return;

      const rawStr = (raw as string) ?? '';

      // Restore prefix if user deleted it
      if (!rawStr.startsWith(country.prefix)) {
        this.form.get('phoneNumber')!.setValue(country.prefix + ' ', { emitEvent: false });
        return;
      }

      // Strip everything non-digit from the local part, limit to max digits
      const afterPrefix = rawStr.slice(country.prefix.length);
      const digitsOnly = afterPrefix.replace(/\D/g, '').slice(0, country.localDigits.max);
      const cleaned = country.prefix + (digitsOnly ? ' ' + digitsOnly : ' ');

      if (cleaned !== rawStr) {
        this.form.get('phoneNumber')!.setValue(cleaned, { emitEvent: false });
      }
    });
  }

  private validatePhone(control: AbstractControl): ValidationErrors | null {
    const raw = (control.value as string ?? '').trim();
    if (!raw) return null;
    const countryCode = this.form?.get('country')?.value as string;
    if (!countryCode) return null;
    const country = EU_COUNTRIES.find(c => c.value === countryCode);
    if (!country) return null;

    // If the local part (after stripping prefix) is empty, the user hasn't typed a number yet.
    // Return phoneInvalid to block submit — required passes for "+43 " (non-empty string)
    // but isValidPhoneNumber would throw/reject, so we short-circuit cleanly here.
    const localPart = raw.startsWith(country.prefix)
      ? raw.slice(country.prefix.length).trim()
      : raw;
    if (!localPart) return { phoneInvalid: true };

    try {
      if (isValidPhoneNumber(raw, countryCode as CountryCode)) return null;

      // ★★ POR QUÉ FALLA, NO SÓLO QUE FALLA. Un único mensaje — «no coincide con el formato esperado»
      // — obliga al usuario a adivinar entre tres causas muy distintas: faltan dígitos, sobran, o los
      // dígitos son los que son pero ningún número de ese país empieza así. Un caso real terminó en
      // «creo que espera que ponga los espacios», que es lo Único que el campo NO puede estar pidiendo:
      // borra todo lo que no sea dígito mientras se escribe.
      //
      // ★ La longitud y la validez son dos preguntas separadas en la librería, y aquí también:
      // `undefined` significa que la longitud es correcta, así que si aun así no vale, el problema está
      // en los dígitos y no en cuántos hay.
      switch (validatePhoneNumberLength(raw, countryCode as CountryCode)) {
        case 'TOO_SHORT':
          return { phoneTooShort: true };
        case 'TOO_LONG':
          return { phoneTooLong: true };
        default:
          return { phoneInvalid: true };
      }
    } catch {
      return { phoneInvalid: true };
    }
  }

  fieldError(name: string): string {
    const ctrl = this.form.get(name);
    if (!ctrl || !ctrl.invalid || !ctrl.touched) return '';
    if (ctrl.hasError('phoneTooShort')) return 'QUALIFY.PHONE_TOO_SHORT';
    if (ctrl.hasError('phoneTooLong')) return 'QUALIFY.PHONE_TOO_LONG';
    if (ctrl.hasError('phoneInvalid')) return 'QUALIFY.PHONE_INVALID';
    if (ctrl.hasError('required') || ctrl.hasError('requiredTrue')) return 'VALIDATION.REQUIRED';
    return 'VALIDATION.INVALID';
  }

  submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.error.set(null);

    const { country, phoneNumber, howHeardAboutUs, salesVolumeRange, currentSystem, legalAccepted } =
      this.form.getRawValue();

    this.onboardingService.qualify({
      country, phoneNumber, howHeardAboutUs, salesVolumeRange, currentSystem, legalAccepted,
    }).subscribe({
      next: () => {
        this.currentUser.refresh().subscribe(() => {
          void this.router.navigateByUrl('/onboarding/plan');
        });
      },
      error: (err) => {
        this.error.set(err?.error?.message ?? 'ERRORS.GENERIC');
        this.submitting.set(false);
      },
    });
  }
}
