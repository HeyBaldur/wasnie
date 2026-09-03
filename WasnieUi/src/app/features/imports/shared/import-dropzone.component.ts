import { Component, input, output, signal } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import { IconComponent } from '../../../shared/components/icon/icon.component';

/**
 * The file picker every import wizard uses.
 *
 * ★★ IT EXISTS BECAUSE THERE WERE THREE COPIES OF IT. The transactions import, the transactions
 * update and the payees import each carried their own `.drop-zone` markup and their own near
 * identical stylesheet. Restyling them one at a time is exactly how five hand-written copies of the
 * scrollbar recipe came to exist elsewhere in this app, so the new look landed in one component and
 * the three steps now render it.
 *
 * ★★ IT OWNS NO STATE THAT MATTERS AND CALLS NOTHING. Validation, parsing, limits and error
 * messages stay in the step that hosts it — this only reports the file the user chose. That is what
 * kept the change visual: no service moved, no API call moved, no step's logic was rewritten.
 *
 * ★ THE REFERENCE WAS TAILWIND AND THIS IS NOT (§5.5). The design it copies is Untitled UI's file
 * uploader, whose markup is `ring-secondary`, `bg-primary`, `text-brand-secondary` and raw hex in
 * the SVGs. None of that can ship here: every colour, radius, gap and font size below is a token.
 * What was copied is the LAYOUT and the hierarchy — a small framed icon, a brand-coloured call to
 * action beside plain "or drag and drop" text, a quiet constraints line, and the chosen file as a
 * card BELOW the zone rather than replacing it.
 */
@Component({
  selector: 'app-import-dropzone',
  standalone: true,
  imports: [TranslateModule, IconComponent],
  templateUrl: './import-dropzone.component.html',
  styleUrl: './import-dropzone.component.scss',
})
export class ImportDropzoneComponent {
  /** Accept attribute for the native input, e.g. ".csv,.xlsx". */
  readonly accept = input<string>('.csv,.xlsx');

  /** Translation key for the line under the call to action (formats, size limits). */
  readonly hintKey = input<string>('');

  /** The chosen file, owned by the host step so it stays the single source of truth. */
  readonly file = input<File | null>(null);

  /**
   * Whether the host is busy with this file.
   *
   * ★ IT DRIVES A REAL INDETERMINATE BAR, NOT A PERCENTAGE. The reference shows "50%" against a
   * filled track, and this upload has no percentage to show: the file goes up in one request and
   * the server answers when it has parsed it. Painting a number would be inventing progress — the
   * same objection the engine's screens make about inventing figures. The bar animates instead.
   */
  readonly busy = input<boolean>(false);

  /** A file the user picked, before the host has validated it. */
  readonly fileSelected = output<File>();

  /** The user cleared the chosen file. */
  readonly cleared = output<void>();

  readonly isDragging = signal(false);

  onDragOver(e: DragEvent): void {
    e.preventDefault();
    this.isDragging.set(true);
  }

  onDragLeave(): void {
    this.isDragging.set(false);
  }

  onDrop(e: DragEvent): void {
    e.preventDefault();
    this.isDragging.set(false);
    const file = e.dataTransfer?.files[0];
    if (file) this.fileSelected.emit(file);
  }

  onFileInput(e: Event): void {
    const input = e.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) this.fileSelected.emit(file);
    // Reset so choosing the SAME file twice still fires a change event.
    input.value = '';
  }

  /**
   * ★ THE BADGE IS THE EXTENSION, NOT A HAND-DRAWN GLYPH PER TYPE. The reference ships a bespoke
   * 40×40 SVG for each file type with the label baked into the path data. Two formats reach this
   * screen — CSV and XLSX — so the badge renders the extension as text over one framed document
   * icon. It cannot go stale when a third format is accepted.
   */
  extension(file: File): string {
    const ext = file.name.split('.').pop() ?? '';
    return ext.slice(0, 4).toUpperCase();
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}
