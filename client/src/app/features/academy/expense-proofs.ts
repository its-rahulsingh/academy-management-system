import { Component, inject, input, output, signal } from '@angular/core';
import { Api, apiUrl, errorMessage } from '../../core/api';
import { Expense, ExpenseProofFile } from '../../core/models';
import { fileSize, readProofFile } from '../../shared/proof-files';
import { UI } from '../../shared/ui';

/** The bills and receipts attached to one expense: view, add more, remove one. */
@Component({
  selector: 'app-expense-proofs',
  imports: [UI],
  template: `
    <app-modal [title]="'Proof — ' + expense().title" saveLabel="" [wide]="true" [error]="error()" (closed)="closed.emit()">
      <p class="helper">
        {{ expense().amount | money: true }} · {{ expense().spentOn | day }} · {{ expense().category }}
        @if (expense().status === 'Voided') {
          · <b>Voided</b>: its proof stays on record and can no longer change.
        }
      </p>

      @if (loading()) {
        <p class="helper">Loading…</p>
      } @else if (!files().length) {
        <div class="proof-empty">No bill or receipt attached yet.</div>
      }

      <div class="proof-grid">
        @for (f of files(); track f.id) {
          <article class="proof-card">
            <a class="proof-thumb" [href]="url(f.id)" target="_blank" rel="noopener" [attr.aria-label]="'Open ' + f.fileName">
              @if (f.contentType.startsWith('image/')) {
                <img [src]="url(f.id)" [alt]="f.fileName" loading="lazy" />
              } @else {
                <span class="pdf-icon" aria-hidden="true">PDF</span>
              }
            </a>
            <div class="proof-meta">
              <strong [title]="f.fileName">{{ f.fileName }}</strong>
              <small>{{ size(f.sizeBytes) }} · {{ f.createdAtUtc | day }}{{ f.addedBy ? ' · ' + f.addedBy : '' }}</small>
              <div class="proof-actions">
                <a class="table-action" [href]="url(f.id)" target="_blank" rel="noopener">Open</a>
                @if (editable()) {
                  <button type="button" class="table-action danger-text" [disabled]="busy()" (click)="remove(f)">Remove</button>
                }
              </div>
            </div>
          </article>
        }
      </div>

      @if (editable() && files().length < maxProofs()) {
        <label class="proof-drop">
          <input type="file" accept="image/*,application/pdf" multiple (change)="add($event)" [disabled]="busy()" />
          <span>{{ busy() ? 'Uploading…' : '＋ Attach a photo or PDF' }}</span>
          <small>Bill, invoice or payment screenshot · up to {{ maxMb() }} MB each · {{ maxProofs() }} files per expense</small>
        </label>
      }
    </app-modal>
  `,
})
export class ExpenseProofsComponent {
  private readonly api = inject(Api);
  readonly expense = input.required<Expense>();
  /** Whether this user may add and remove files (and the expense is not voided). */
  readonly canWrite = input(false);
  readonly maxProofs = input(5);
  readonly maxMb = input(5);
  readonly closed = output<void>();
  /** The number of files changed, so the list can update its 📎 count. */
  readonly changed = output<number>();

  protected readonly files = signal<ExpenseProofFile[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  ngOnInit(): void {
    this.load();
  }

  protected editable(): boolean {
    return this.canWrite() && this.expense().status !== 'Voided';
  }

  protected url(proofId: string): string {
    return apiUrl(`expenses/${this.expense().id}/proofs/${proofId}`);
  }

  protected size = fileSize;

  private load(): void {
    this.api.get<ExpenseProofFile[]>(`expenses/${this.expense().id}/proofs`).subscribe({
      next: files => {
        this.files.set(files);
        this.loading.set(false);
        this.changed.emit(files.length);
      },
      error: e => {
        this.loading.set(false);
        this.error.set(errorMessage(e));
      },
    });
  }

  protected async add(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const chosen = Array.from(input.files ?? []).slice(0, this.maxProofs() - this.files().length);
    input.value = '';
    if (!chosen.length) return;
    this.busy.set(true);
    this.error.set('');
    try {
      for (const file of chosen) {
        const proof = await readProofFile(file, this.maxMb());
        await new Promise<void>((resolve, reject) =>
          this.api.post(`expenses/${this.expense().id}/proofs`, { fileName: proof.fileName, dataUrl: proof.dataUrl }).subscribe({
            next: () => resolve(),
            error: e => reject(new Error(errorMessage(e))),
          }),
        );
      }
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'The file could not be attached.');
    } finally {
      this.busy.set(false);
      this.load();
    }
  }

  protected remove(file: ExpenseProofFile): void {
    if (!confirm(`Remove ${file.fileName} from this expense? This shows in the activity log.`)) return;
    this.busy.set(true);
    this.error.set('');
    this.api.delete(`expenses/${this.expense().id}/proofs/${file.id}`).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: e => {
        this.busy.set(false);
        this.error.set(errorMessage(e));
      },
    });
  }
}
