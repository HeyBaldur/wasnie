import { Component, input } from '@angular/core';

/**
 * La bandera de un idioma, dibujada en línea.
 *
 * ★★ EXISTE PARA QUE HAYA UNA SOLA COPIA. Los SVG vivían dentro de `LanguageToggleComponent`, el
 * selector de las pantallas sin sesión. Cuando Ajustes → Apariencia tuvo que mostrar las mismas
 * banderas, copiarlas habría dejado dos juegos que se separan en cuanto alguien retoque uno: dos
 * controles del MISMO idioma, visibles en la misma sesión, dibujados distinto. Es un componente y no
 * un partial de SCSS porque lo que se comparte es marcado, no estilo.
 *
 * ★ NO VA AL REGISTRO DE ICONOS. Ese registro es de iconos de línea, monocromos, 24×24 y pintados con
 * `currentColor`; una bandera es rellena, multicolor y tiene su propia proporción. Meterla ahí
 * convertiría en mentira el contrato de todos los demás iconos.
 *
 * ★ LOS COLORES SON LITERALES A PROPÓSITO, y es la excepción a §5.5. El rojo de la bandera de España
 * es el rojo de la bandera de España: no es una decisión de diseño de este producto, no cambia con el
 * tema y no hay token que pueda representarlo sin mentir.
 */
@Component({
  selector: 'app-language-flag',
  standalone: true,
  templateUrl: './language-flag.component.html',
  styleUrl: './language-flag.component.scss',
})
export class LanguageFlagComponent {
  /** Código de idioma de ngx-translate: `en` | `es` | `pl`. Cualquier otro cae en la bandera inglesa. */
  readonly code = input<string>('en');
}
