import { AfterViewInit, Component, ElementRef, NgZone, OnDestroy, computed, inject, signal } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api';
import { CONTACT, whatsappLink } from '../../core/contact';
import { PublicPlan } from '../../core/models';

const INR = new Intl.NumberFormat('en-IN', { maximumFractionDigits: 0 });
const money = (amount: number) => `₹${INR.format(amount)}`;
const PER: Record<string, string> = { Monthly: '/month', Quarterly: '/quarter', Yearly: '/year' };
const MONTHS: Record<string, number> = { Monthly: 1, Quarterly: 3, Yearly: 12 };

function limit(value: number | null, one: string, many = one + 's'): string {
  return value == null ? `unlimited ${many}` : `${value} ${value === 1 ? one : many}`;
}

function joinAnd(items: string[]): string {
  return items.length < 2 ? items.join('') : `${items.slice(0, -1).join(', ')} and ${items[items.length - 1]}`;
}

/** The bullet list on a price card, written from the plan's limits. */
function features(p: PublicPlan): string[] {
  const list = [
    p.maxStudents == null ? 'Unlimited students' : `${p.maxStudents} students`,
    p.maxFaculty == null ? 'Unlimited faculty' : `${p.maxFaculty} faculty`,
    p.maxUsers == null ? 'Unlimited staff logins' : p.maxUsers === 1 ? '1 login (you)' : `${p.maxUsers} staff logins`,
    p.maxReceiptEmailsPerMonth == null ? 'Unlimited emailed receipts' : `${p.maxReceiptEmailsPerMonth} emailed receipts a month`,
    p.customBranding ? 'Your own logo' : '"Powered by Ojas" on forms',
  ];
  return list;
}

/** Questions whose answers do not depend on the price list. */
const FIXED_FAQS: string[][] = [
  ['Can you bring in our existing data?', 'Yes. Share your student and fee lists in Excel and we will help you load them, so you start with your real numbers.'],
  ['Is our data private?', 'Every academy has its own workspace and only sees its own records. Inside your academy, each role sees only what it needs.'],
  ['Can teachers fake attendance?', 'Not easily. QR codes change every 20 seconds and expire within a minute. Location check-ins and manual entries wait for your approval.'],
  ['Do students see our fee details?', 'No. The student portal shows subjects, teachers, test results and report cards. Payment receipts go to the student by email.'],
];

/** "Start free" buttons open WhatsApp with a ready-made message. */
const CONTACT_URL = whatsappLink('Hi, I would like to start using Ojas for my academy.');

/**
 * Public marketing page. Everything visual is drawn in code (a particle orb on a canvas, CSS 3D product mock-ups)
 * and the two "videos" are the animated product tour and ad from /media, embedded on a loop.
 * Figures shown in mock-ups are illustrative.
 */
@Component({
  selector: 'app-landing',
  imports: [RouterLink],
  template: `
    <div class="lp">
      <!-- NAV -->
      <header class="nav" [class.scrolled]="scrolled()">
        <button class="brand" (click)="go('top')"><img src="ojas-logo.svg" alt="" /><span>Ojas</span></button>
        <nav>
          <button (click)="go('features')">Features</button>
          <button (click)="go('tour')">Tour</button>
          <button (click)="go('security')">Security</button>
          <button (click)="go('pricing')">Pricing</button>
          <button (click)="go('faq')">FAQ</button>
        </nav>
        <div class="nav-cta">
          <a class="ghost" routerLink="/login">Sign in</a>
          <button class="btn glow" (click)="start()">Start free</button>
        </div>
      </header>

      <!-- HERO -->
      <section class="hero" id="top">
        <canvas class="orb" #orb aria-hidden="true"></canvas>
        <div class="aurora" aria-hidden="true"></div>
        <div class="hero-copy">
          @if (pricePill()) {
            <span class="lp-pill rv in"><i></i> {{ pricePill() }}</span>
          }
          <p class="lp-tag rv in">Powering how Bharat learns</p>
          <h1 class="rv">The radiant way to <br class="br-d" /><span class="grad">run your academy.</span></h1>
          <p class="rv">
            Fees, faculty attendance, admissions, student portals and reports — one beautifully simple app,
            built for coaching institutes and academies across India.
          </p>
          <div class="hero-actions rv">
            <button class="btn glow lg" (click)="start()">Start free today <b>→</b></button>
            <button class="btn glass lg" (click)="openTour()"><span class="play">▶</span> Watch the 2-min tour</button>
          </div>
          <ul class="hero-points rv">
            <li>No card needed</li><li>We set it up for you</li><li>Works on any phone</li>
          </ul>
        </div>

        <!-- 3D product stage -->
        <div class="stage" #stage>
          <div class="dash">
            <div class="dash-bar"><i></i><i></i><i></i><span>🔒 Sunrise Classes · Ojas</span></div>
            <div class="dash-body">
              <aside>
                <div class="acad"><b class="am"></b><div><strong>Sunrise Classes</strong><small>Super Admin</small></div></div>
                @for (n of nav; track n; let i = $index) {
                  <div class="nv" [class.on]="i === 0">{{ n }}</div>
                }
              </aside>
              <div class="dash-main">
                <small class="crumb">OVERVIEW</small>
                <h4>Dashboard</h4>
                <div class="dstats">
                  <div><small>Fees collected</small><b class="g">₹4,82,500</b></div>
                  <div><small>Fees due</small><b class="r">₹1,26,000</b></div>
                  <div><small>Expenses</small><b class="o">₹96,400</b></div>
                  <div><small>Active students</small><b>312</b></div>
                </div>
                <div class="dash-row">
                  <div class="panel chart">
                    <small>Fee collection by month</small>
                    <div class="bars">
                      @for (h of bars; track $index; let i = $index) {
                        <span [style.height.%]="h" [style.animation-delay.ms]="i * 90" [class.hi]="i === bars.length - 1"></span>
                      }
                    </div>
                  </div>
                  <div class="panel live">
                    <small>Classes running now</small>
                    @for (c of running; track c[0]) {
                      <div class="lv"><i></i><div><b>{{ c[0] }}</b><span>{{ c[1] }}</span></div><em>{{ c[2] }}</em></div>
                    }
                  </div>
                </div>
              </div>
            </div>
          </div>
          <div class="float f1"><span class="ic gr">✉</span><div><b>Receipt R-0142 emailed</b><small>Priya Patel · ₹8,500 · balance ₹0</small></div></div>
          <div class="float f2"><span class="ic vi">◉</span><div><b>Mr. Iyer clocked in</b><small>Physics · approved by QR</small></div></div>
          <div class="float f3"><span class="ic am">✦</span><div><b>New admission</b><small>Ananya Verma · NEET Class 11</small></div></div>
          <div class="float f4"><span class="ic rd">!</span><div><b>Agreed fee lowered</b><small>Needs your review</small></div></div>
        </div>
      </section>

      <!-- AUDIENCE MARQUEE -->
      <section class="marquee" aria-label="Built for">
        <div class="track">
          @for (k of [0, 1]; track k) {
            @for (a of audiences; track a) { <span>{{ a }}</span><i>✦</i> }
          }
        </div>
      </section>

      <!-- PROBLEM -->
      <section class="sec problem">
        <div class="eyebrow rv">THE OLD WAY</div>
        <h2 class="rv">Registers get lost. Excel lies.<br /><span class="dim">WhatsApp forgets.</span></h2>
        <div class="pains">
          @for (p of pains; track p[0]; let i = $index) {
            <div class="pain rv" [style.transition-delay.ms]="i * 120">
              <span class="emo">{{ p[0] }}</span><s>{{ p[1] }}</s><p>{{ p[2] }}</p>
            </div>
          }
        </div>
      </section>

      <!-- FEATURES BENTO -->
      <section class="sec" id="features">
        <div class="eyebrow rv">EVERYTHING IN ONE PLACE</div>
        <h2 class="rv">One app. <span class="grad">Every corner</span> of your academy.</h2>
        <p class="lead rv">Ten tools that usually live in ten places — working together, on every device.</p>

        <div class="bento">
          <article class="tile t-fees tilt rv">
            <div class="tx"><span class="tag">Fees & receipts</span><h3>Collect fees without the chasing.</h3>
              <p>Instalments, discounts, surcharges and refunds. Numbered receipts, emailed to the student the moment you record a payment.</p></div>
            <div class="vis rcpt">
              <div class="rc-h"><small>RECEIPT</small><b>R-0142</b></div>
              <div class="rc-l"><span>Student</span><b>Priya Patel</b></div>
              <div class="rc-l"><span>Paid now</span><b>₹8,500 · UPI</b></div>
              <div class="rc-l"><span>Balance due</span><b class="g">₹0</b></div>
              <div class="rc-ok">✉ Emailed to the student</div>
            </div>
          </article>

          <article class="tile t-qr tilt rv">
            <div class="tx"><span class="tag">Faculty attendance</span><h3>Attendance that can’t be faked.</h3>
              <p>Teachers scan a QR that changes every 20 seconds — or clock in by location. Hourly pay is worked out to the minute.</p></div>
            <div class="vis qrbox">
              <div class="qr">
                @for (on of qr; track $index) { <i [class.on]="on"></i> }
                <span class="scan"></span>
              </div>
              <div class="timer"><i></i>Physics · Class 11 <b>01:32:10</b></div>
            </div>
          </article>

          <article class="tile tilt rv">
            <span class="tag">Online admissions</span><h3>Admissions from a QR poster.</h3>
            <p>Parents apply from their phone. You approve in one tap — the student is created for you.</p>
            <div class="vis mini-list">
              <div><b>Ananya Verma</b><em class="ok">Approved</em></div>
              <div><b>Dev Malhotra</b><em>Approve</em></div>
            </div>
          </article>

          <article class="tile tilt rv">
            <span class="tag">Student portal</span><h3>Report cards on their phone.</h3>
            <p>Subjects, teachers, test results and the full report card.</p>
            <div class="vis meters">
              @for (m of marks; track m[0]) {
                <div><span>{{ m[0] }}</span><i><b [style.width.%]="m[1]"></b></i><em>{{ m[1] }}</em></div>
              }
            </div>
          </article>

          <article class="tile tilt rv">
            <span class="tag">Faculty pay</span><h3>Package or hourly. Always right.</h3>
            <p>Payouts can never exceed what is owed.</p>
            <div class="vis formula">85 min <i>×</i> ₹600 <i>÷ 60 =</i> <b>₹850</b></div>
          </article>

          <article class="tile tilt rv">
            <span class="tag">Reports</span><h3>Day, week or month — in Excel.</h3>
            <p>Plus a daily activity report of who did what.</p>
            <div class="vis xl"><span class="xls">XLSX</span>
              <div class="pairs">@for (h of pairBars; track $index) { <span><i [style.height.%]="h[0]"></i><i [style.height.%]="h[1]"></i></span> }</div>
            </div>
          </article>

          <article class="tile tilt rv">
            <span class="tag">Alerts</span><h3>Know when something looks off.</h3>
            <p>Red, orange and yellow flags for risky actions.</p>
            <div class="vis flags">
              <div><i class="r"></i>Agreed fee lowered</div><div><i class="o"></i>Staff active at 11:40 PM</div><div><i class="y"></i>3 wrong passwords</div>
            </div>
          </article>

          <article class="tile tilt rv">
            <span class="tag">Expenses</span><h3>Every rupee spent, with a bill.</h3>
            <p>Attach up to five bills. Require proof above any amount.</p>
            <div class="vis bills"><span></span><span></span><span></span></div>
          </article>

          <article class="tile tilt rv">
            <span class="tag">New year</span><h3>Promote a whole batch in minutes.</h3>
            <p>Unpaid balances carry forward automatically.</p>
            <div class="vis years"><b>2026-27</b><i>→</i><b class="nx">2027-28</b></div>
          </article>

          <article class="tile tilt rv">
            <span class="tag">Your brand</span><h3>Looks like your academy.</h3>
            <p>Your logo and tagline on Basic and Pro. Emails in your name on every plan.</p>
            <div class="vis brandv"><b class="am big"></b><div><strong>Sunrise Classes</strong><small>Where toppers are made</small></div></div>
          </article>
        </div>
      </section>

      <!-- VIDEO / TOUR -->
      <section class="sec tour" id="tour">
        <div class="eyebrow rv">SEE IT IN ACTION</div>
        <h2 class="rv">Two minutes. <span class="grad">The whole academy.</span></h2>
        <p class="lead rv">Watch how a day at an academy looks on Ojas — from admission to report card.</p>
        <div class="screens rv">
          <button class="laptop" (click)="openTour()" aria-label="Play the product tour">
            <div class="lid"><iframe [src]="tourEmbed" title="Ojas product tour" loading="lazy" tabindex="-1"></iframe></div>
            <div class="base"></div>
            <span class="big-play">▶</span>
          </button>
          <div class="phone-frame">
            <iframe [src]="adEmbed" title="Ojas 30-second reel" loading="lazy" tabindex="-1"></iframe>
          </div>
        </div>
        <div class="chapters rv">
          @for (c of chapters; track c) { <span>{{ c }}</span> }
        </div>
      </section>

      <!-- HOW IT WORKS -->
      <section class="sec">
        <div class="eyebrow rv">GOING LIVE</div>
        <h2 class="rv">Up and running this week.</h2>
        <div class="steps">
          @for (s of steps; track s[0]; let i = $index) {
            <div class="step rv" [style.transition-delay.ms]="i * 120">
              <span class="num">{{ i + 1 }}</span><h3>{{ s[0] }}</h3><p>{{ s[1] }}</p>
            </div>
          }
        </div>
      </section>

      <!-- STATS -->
      <section class="statband">
        @for (s of stats; track s[1]) {
          <div class="lstat rv"><b>{{ s[0] }}</b><span>{{ s[1] }}</span></div>
        }
      </section>

      <!-- SECURITY -->
      <section class="sec" id="security">
        <div class="eyebrow rv">TRUST, BUILT IN</div>
        <h2 class="rv">Your academy’s data stays <span class="grad">yours.</span></h2>
        <div class="sec-grid">
          @for (s of security; track s[1]; let i = $index) {
            <div class="sg tilt rv" [style.transition-delay.ms]="(i % 3) * 100"><span class="sgi">{{ s[0] }}</span><h3>{{ s[1] }}</h3><p>{{ s[2] }}</p></div>
          }
        </div>
      </section>

      <!-- PRICING -->
      <section class="sec" id="pricing">
        <div class="eyebrow rv">PRICING</div>
        <h2 class="rv">Start free. <span class="grad">Pay only as you grow.</span></h2>
        <p class="lead rv">{{ freePlan() ? 'Every academy starts on ' + freePlan()!.name + ' and can stay there for as long as it likes. Upgrade only when you need more. No card needed.' : 'Simple monthly plans. Upgrade only when you need more.' }}</p>
        <div class="prices">
          @for (p of plans(); track p.name) {
            <div class="price tilt rv in" [class.pop]="p.highlighted">
              @if (p.highlighted) { <span class="badge">Most popular</span> }
              <small>{{ p.name }}</small>
              <h3>{{ p.price }}<span>{{ p.per }}</span></h3>
              <p>{{ p.forWho }}</p>
              @if (p.yearly) { <div class="free">{{ p.yearly }}</div> }
              <ul class="pfeat">
                @for (f of p.features; track f) { <li>{{ f }}</li> }
              </ul>
              <button class="btn" [class.glow]="p.highlighted" [class.glass]="!p.highlighted" (click)="start()">{{ p.free ? 'Start free' : 'Choose ' + p.name }}</button>
            </div>
          } @empty {
            <p class="lead">{{ plansFailed() ? 'Plans could not be loaded just now. Message us and we will share them.' : 'Loading plans…' }}</p>
          }
        </div>
        <ul class="includes rv">
          @for (f of includes; track f) { <li>{{ f }}</li> }
        </ul>
      </section>

      <!-- FAQ -->
      <section class="sec faq" id="faq">
        <div class="eyebrow rv">QUESTIONS</div>
        <h2 class="rv">Good questions, straight answers.</h2>
        <div class="qa">
          @for (q of faqs(); track q[0]) {
            <details class="rv"><summary>{{ q[0] }}<i>+</i></summary><p>{{ q[1] }}</p></details>
          }
        </div>
      </section>

      <!-- FINAL CTA -->
      <section class="final" id="start">
        <div class="final-glow" aria-hidden="true"></div>
        <img class="final-logo rv" src="ojas-logo.svg" alt="" />
        <h2 class="rv">Give your academy its <span class="grad">Ojas.</span></h2>
        <p class="rv">Free to start. We set everything up and walk you through it.</p>
        <div class="hero-actions rv">
          @if (contactUrl) {
            <a class="btn glow lg" [href]="contactUrl" target="_blank" rel="noopener">Book your free setup on WhatsApp <b>→</b></a>
          }
          <a class="btn glass lg" routerLink="/login">Already with us? Sign in</a>
        </div>
        <div class="contact rv">
          <a [href]="contact.tel"><span>📞</span><div><small>Call us</small><b>{{ contact.phone }}</b></div></a>
          <a [href]="'mailto:' + contact.email"><span>✉️</span><div><small>Email us</small><b>{{ contact.email }}</b></div></a>
          <a [href]="contactUrl" target="_blank" rel="noopener"><span>💬</span><div><small>WhatsApp</small><b>{{ contact.phone }}</b></div></a>
        </div>
      </section>

      <footer class="foot">
        <div class="brand"><img src="ojas-logo.svg" alt="" /><span>Ojas</span></div>
        <p class="foot-tag">Powering how Bharat learns.</p>
        <p>Ojas — Sanskrit for vitality and radiance.</p>
        <p class="foot-contact"><a [href]="contact.tel">{{ contact.phone }}</a> · <a [href]="'mailto:' + contact.email">{{ contact.email }}</a></p>
        <small>© {{ year }} Ojas · Figures in previews are illustrative.</small>
      </footer>

      <!-- TOUR MODAL -->
      @if (tourOpen()) {
        <div class="tour-modal" (click)="tourOpen.set(false)" role="dialog" aria-label="Ojas product tour">
          <div class="modal-box" (click)="$event.stopPropagation()">
            <button class="close" (click)="tourOpen.set(false)" aria-label="Close">✕</button>
            <iframe [src]="tourEmbed" title="Ojas product tour" allow="autoplay"></iframe>
          </div>
        </div>
      }
    </div>
  `,
  styles: `
    :host { display: block; --bg: #05040f; --ink: #f4f3ff; --dim: #a5a3c7; --line: #ffffff14; --vi: #7b6cff; --vi2: #5147df; --am: #ffb547; --cy: #5ee6ff; }
    .lp { background: var(--bg); color: var(--ink); font-family: Manrope, 'DM Sans', system-ui, sans-serif; overflow-x: clip; }
    button { font: inherit; color: inherit; background: none; border: 0; cursor: pointer; }
    a { color: inherit; text-decoration: none; }
    .grad { background: linear-gradient(100deg, #b7adff 0%, #7b6cff 40%, #ffb547 100%); -webkit-background-clip: text; background-clip: text; color: transparent; }
    .dim { color: #6f6d92; }

    /* buttons */
    .btn { display: inline-flex; align-items: center; gap: 10px; border-radius: 999px; padding: 13px 22px; font-weight: 800; font-size: 14px; transition: transform .2s, box-shadow .2s, background .2s; white-space: nowrap; }
    .btn.lg { padding: 17px 28px; font-size: 16px; }
    .btn.glow { background: linear-gradient(135deg, #8b7dff, #5147df); color: #fff; box-shadow: 0 10px 40px #6a5cf266, inset 0 1px 0 #ffffff40; }
    .btn.glow:hover { transform: translateY(-2px); box-shadow: 0 16px 60px #6a5cf299, inset 0 1px 0 #ffffff40; }
    .btn.glass { background: #ffffff0d; border: 1px solid #ffffff22; color: #fff; backdrop-filter: blur(10px); }
    .btn.glass:hover { background: #ffffff1a; transform: translateY(-2px); }
    .play { display: grid; place-items: center; width: 26px; height: 26px; border-radius: 50%; background: #fff; color: var(--vi2); font-size: 10px; }

    /* nav */
    .nav { position: fixed; top: 14px; left: 50%; transform: translateX(-50%); z-index: 50; width: min(1180px, calc(100% - 24px)); display: flex; align-items: center; justify-content: space-between; gap: 16px;
      padding: 10px 12px 10px 18px; border-radius: 999px; border: 1px solid transparent; transition: background .3s, border-color .3s; }
    .nav.scrolled { background: #0b0a1fcc; border-color: var(--line); backdrop-filter: blur(16px); }
    .brand { display: flex; align-items: center; gap: 10px; font-weight: 800; font-size: 20px; letter-spacing: -.03em; }
    .brand img { width: 32px; height: 32px; }
    .nav nav { display: flex; gap: 4px; }
    .nav nav button { padding: 8px 14px; border-radius: 999px; font-size: 14px; font-weight: 600; color: var(--dim); }
    .nav nav button:hover { color: #fff; background: #ffffff0f; }
    .nav-cta { display: flex; align-items: center; gap: 8px; }
    .ghost { padding: 10px 14px; font-size: 14px; font-weight: 700; color: var(--dim); }
    .ghost:hover { color: #fff; }

    /* hero */
    .hero { position: relative; min-height: 100vh; padding: 150px 20px 40px; text-align: center; perspective: 1600px; }
    .orb { position: absolute; inset: 0; width: 100%; height: 100%; z-index: 0; }
    .aurora { position: absolute; inset: -10% -10% 30%; z-index: 0; pointer-events: none;
      background: radial-gradient(40% 35% at 20% 20%, #5147df55, transparent 70%), radial-gradient(35% 30% at 85% 15%, #ffb54722, transparent 70%), radial-gradient(45% 40% at 50% 60%, #2a1f8f44, transparent 70%);
      filter: blur(20px); animation: drift 18s ease-in-out infinite alternate; }
    @keyframes drift { to { transform: translate3d(3%, 4%, 0) scale(1.08); } }
    .hero-copy { position: relative; z-index: 2; max-width: 900px; margin: 0 auto; }
    .lp-pill { display: inline-flex; align-items: center; gap: 10px; padding: 8px 16px; border-radius: 999px; font-size: 13px; font-weight: 700; color: #d9d5ff;
      background: #ffffff0a; border: 1px solid #ffffff1f; backdrop-filter: blur(8px); }
    .lp-tag { margin: 24px 0 -4px; font-size: 13px; font-weight: 800; letter-spacing: .22em; text-transform: uppercase;
      background: linear-gradient(90deg, #c4bbff, #f5c26b); -webkit-background-clip: text; background-clip: text; color: transparent; }
    .lp-pill i { width: 8px; height: 8px; border-radius: 50%; background: var(--am); box-shadow: 0 0 12px var(--am); animation: blink 2s infinite; }
    @keyframes blink { 50% { opacity: .35; } }
    h1 { font-size: clamp(44px, 7.4vw, 96px); line-height: .98; letter-spacing: -.055em; font-weight: 800; margin: 26px 0 22px; }
    .hero-copy > p { font-size: clamp(16px, 1.6vw, 20px); line-height: 1.6; color: var(--dim); max-width: 680px; margin: 0 auto 32px; }
    .hero-actions { display: flex; gap: 14px; justify-content: center; flex-wrap: wrap; }
    .hero-points { list-style: none; padding: 0; margin: 24px 0 0; display: flex; gap: 22px; justify-content: center; flex-wrap: wrap; font-size: 14px; color: #8f8cb6; font-weight: 600; }
    .hero-points li::before { content: '✓'; color: #6ee7b7; margin-right: 7px; font-weight: 800; }

    /* 3D stage */
    .stage { position: relative; z-index: 2; max-width: 1080px; margin: 70px auto 0; transform-style: preserve-3d;
      transform: rotateX(calc((1 - var(--sp, 0)) * 24deg)) rotateY(calc(var(--tx, 0) * 6deg)) scale(calc(.9 + var(--sp, 0) * .1)); transition: transform .15s linear; }
    .dash { border-radius: 20px; overflow: hidden; background: #fff; color: #1e2438; text-align: left; border: 1px solid #ffffff30;
      box-shadow: 0 60px 140px #000c, 0 0 0 1px #ffffff10, 0 0 120px #5147df55; }
    .dash-bar { display: flex; align-items: center; gap: 7px; padding: 12px 16px; background: #f3f3f9; border-bottom: 1px solid #e8e9f2; }
    .dash-bar i { width: 11px; height: 11px; border-radius: 50%; background: #ff6159; } .dash-bar i:nth-child(2) { background: #ffbd2e; } .dash-bar i:nth-child(3) { background: #28c941; }
    .dash-bar span { margin: 0 auto; font-size: 12px; font-weight: 700; color: #8d91a3; background: #fff; padding: 4px 30px; border-radius: 999px; }
    .dash-body { display: grid; grid-template-columns: 200px 1fr; min-height: 430px; }
    aside { border-right: 1px solid #eceef5; padding: 16px 10px; }
    .acad { display: flex; gap: 9px; align-items: center; padding: 0 6px 16px; }
    .acad strong { display: block; font-size: 13px; } .acad small { font-size: 10px; color: #9ba2b6; }
    .am { display: inline-block; width: 32px; height: 32px; border-radius: 9px; flex-shrink: 0; background: radial-gradient(circle at 50% 75%, #fff 0 32%, transparent 33%), linear-gradient(135deg, #ff9a3c, #f25c2a); }
    .am.big { width: 56px; height: 56px; border-radius: 16px; }
    .nv { font-size: 12px; font-weight: 700; color: #7b8193; padding: 9px 10px; border-radius: 8px; }
    .nv.on { background: #efedff; color: #5147df; }
    .dash-main { padding: 18px 22px; background: #f8f8fc; }
    .crumb { font-size: 9px; font-weight: 800; color: #9699ad; letter-spacing: 1px; }
    .dash-main h4 { margin: 4px 0 14px; font-size: 20px; letter-spacing: -.03em; }
    .dstats { display: grid; grid-template-columns: repeat(4, 1fr); gap: 10px; }
    .dstats div, .panel { background: #fff; border: 1px solid #eceef5; border-radius: 12px; padding: 12px; }
    .dstats small, .panel > small { display: block; font-size: 10px; font-weight: 700; color: #8d91a3; }
    .dstats b { display: block; font-size: 18px; margin-top: 4px; letter-spacing: -.02em; }
    .g { color: #1fa971; } .r { color: #e5484d; } .o { color: #f08a24; }
    .dash-row { display: grid; grid-template-columns: 1.4fr 1fr; gap: 10px; margin-top: 10px; }
    .bars { display: flex; align-items: flex-end; gap: 8px; height: 150px; margin-top: 12px; }
    .bars span { flex: 1; border-radius: 5px 5px 2px 2px; background: #cdc8fb; transform-origin: bottom; animation: grow 1.2s cubic-bezier(.2,.8,.2,1) both; }
    .bars span.hi { background: linear-gradient(#8b7dff, #5147df); }
    @keyframes grow { from { transform: scaleY(0); } }
    .lv { display: flex; align-items: center; gap: 9px; padding: 9px 0; border-bottom: 1px solid #f1f2f7; }
    .lv > i { width: 8px; height: 8px; border-radius: 50%; background: #1fa971; box-shadow: 0 0 0 4px #1fa97122; animation: blink 1.6s infinite; }
    .lv b { display: block; font-size: 12px; } .lv span { font-size: 10px; color: #8d91a3; } .lv em { margin-left: auto; font-style: normal; font-weight: 800; font-size: 12px; color: #5147df; font-variant-numeric: tabular-nums; }
    .float { position: absolute; display: flex; gap: 12px; align-items: center; padding: 12px 16px 12px 12px; border-radius: 16px; text-align: left;
      background: #12102acc; border: 1px solid #ffffff22; backdrop-filter: blur(14px); box-shadow: 0 20px 50px #000a; animation: float 6s ease-in-out infinite; }
    .float b { display: block; font-size: 13px; } .float small { font-size: 11px; color: var(--dim); }
    .ic { display: grid; place-items: center; width: 36px; height: 36px; border-radius: 11px; font-weight: 800; flex-shrink: 0; }
    .ic.gr { background: #1fa97133; color: #6ee7b7; } .ic.vi { background: #7b6cff33; color: #b7adff; } .ic.am { background: #ffb54733; color: var(--am); } .ic.rd { background: #e5484d33; color: #ff8a8d; }
    .f1 { left: -60px; top: 22%; transform: translateZ(90px); } .f2 { right: -70px; top: 12%; transform: translateZ(120px); animation-delay: -1.5s; }
    .f3 { right: -40px; bottom: 14%; transform: translateZ(70px); animation-delay: -3s; } .f4 { left: -40px; bottom: 8%; transform: translateZ(110px); animation-delay: -4.5s; }
    @keyframes float { 50% { translate: 0 -14px; } }

    /* marquee */
    .marquee { overflow: hidden; border-block: 1px solid var(--line); padding: 22px 0; margin-top: 60px; mask-image: linear-gradient(90deg, transparent, #000 15%, #000 85%, transparent); }
    .track { display: flex; gap: 28px; width: max-content; animation: slide 40s linear infinite; font-size: 18px; font-weight: 700; color: #8f8cb6; white-space: nowrap; }
    .track i { color: var(--vi); font-style: normal; }
    @keyframes slide { to { transform: translateX(-50%); } }

    /* sections */
    .sec { position: relative; max-width: 1200px; margin: 0 auto; padding: 130px 20px 30px; text-align: center; scroll-margin-top: 60px; }
    .eyebrow { font-size: 13px; font-weight: 800; letter-spacing: 4px; color: var(--vi); margin-bottom: 18px; }
    h2 { font-size: clamp(34px, 5vw, 64px); line-height: 1.04; letter-spacing: -.045em; margin: 0 0 18px; font-weight: 800; }
    .lead { font-size: 18px; color: var(--dim); max-width: 620px; margin: 0 auto; line-height: 1.6; }

    .pains { display: grid; grid-template-columns: repeat(3, 1fr); gap: 18px; margin-top: 50px; }
    .pain { padding: 30px; border-radius: 22px; background: #ffffff06; border: 1px solid var(--line); text-align: left; }
    .emo { font-size: 38px; } .pain s { display: block; font-size: 22px; font-weight: 800; margin: 14px 0 8px; text-decoration-color: #e5484d; text-decoration-thickness: 3px; color: #d0cee8; }
    .pain p { margin: 0; color: var(--dim); line-height: 1.6; font-size: 15px; }

    /* bento */
    .bento { display: grid; grid-template-columns: repeat(3, 1fr); gap: 18px; margin-top: 56px; text-align: left; }
    .tile { position: relative; padding: 26px; border-radius: 24px; background: linear-gradient(160deg, #ffffff0c, #ffffff03); border: 1px solid var(--line); overflow: hidden;
      transform: perspective(900px) rotateX(var(--rx, 0deg)) rotateY(var(--ry, 0deg)); transition: transform .25s ease, border-color .25s; }
    .tile::before, .sg::before, .price::before { content: ''; position: absolute; inset: 0; pointer-events: none; opacity: 0; transition: opacity .3s;
      background: radial-gradient(420px circle at var(--mx, 50%) var(--my, 50%), #7b6cff26, transparent 60%); }
    .tile:hover, .sg:hover, .price:hover { border-color: #7b6cff55; } .tile:hover::before, .sg:hover::before, .price:hover::before { opacity: 1; }
    .tag { display: inline-block; font-size: 12px; font-weight: 800; color: var(--am); letter-spacing: 1px; text-transform: uppercase; }
    .tile h3 { font-size: 22px; letter-spacing: -.03em; margin: 10px 0 8px; line-height: 1.2; } .tile p { margin: 0; color: var(--dim); font-size: 14px; line-height: 1.6; }
    .t-fees { grid-column: span 2; display: grid; grid-template-columns: 1fr 300px; gap: 24px; align-items: center; }
    .t-fees h3, .t-qr h3 { font-size: 30px; }
    .t-qr { grid-row: span 2; display: flex; flex-direction: column; justify-content: space-between; gap: 24px; }
    .vis { margin-top: 20px; }
    .rcpt { margin: 0; background: #fff; color: #1e2438; border-radius: 18px; padding: 18px; box-shadow: 0 30px 60px #0008; transform: rotate(3deg); }
    .rc-h { display: flex; justify-content: space-between; margin-bottom: 10px; } .rc-h small { font-weight: 800; letter-spacing: 2px; color: #a0a4b8; font-size: 11px; } .rc-h b { color: #5147df; }
    .rc-l { display: flex; justify-content: space-between; padding: 7px 0; font-size: 13px; border-bottom: 1px dashed #e8e9f2; } .rc-l span { color: #8d91a3; font-weight: 600; }
    .rc-ok { margin-top: 12px; background: #e3f6ee; color: #137a52; font-weight: 800; font-size: 13px; padding: 10px 12px; border-radius: 10px; }
    .qrbox { margin: 0; display: grid; gap: 16px; justify-items: center; }
    .qr { position: relative; display: grid; grid-template-columns: repeat(21, 1fr); width: min(100%, 250px); aspect-ratio: 1; padding: 14px; background: #fff; border-radius: 18px; gap: 0; overflow: hidden; box-shadow: 0 0 60px #7b6cff44; }
    .qr i { background: transparent; } .qr i.on { background: #1e2438; }
    .scan { position: absolute; left: 0; right: 0; height: 4px; background: #1fa971; box-shadow: 0 0 18px #1fa971; animation: scan 2.4s ease-in-out infinite alternate; }
    @keyframes scan { from { top: 8%; } to { top: 90%; } }
    .timer { display: flex; align-items: center; gap: 10px; width: 100%; padding: 14px 16px; border-radius: 14px; background: #ffffff0a; border: 1px solid var(--line); font-size: 14px; font-weight: 700; color: var(--dim); }
    .timer i { width: 9px; height: 9px; border-radius: 50%; background: #1fa971; } .timer b { margin-left: auto; color: #fff; font-size: 18px; font-variant-numeric: tabular-nums; }
    .mini-list div { display: flex; justify-content: space-between; align-items: center; padding: 10px 12px; border-radius: 12px; background: #ffffff08; margin-top: 8px; font-size: 14px; }
    .mini-list em { font-style: normal; font-size: 12px; font-weight: 800; padding: 5px 12px; border-radius: 999px; background: #5147df; } .mini-list em.ok { background: #1fa97133; color: #6ee7b7; }
    .meters div { display: grid; grid-template-columns: 80px 1fr 30px; gap: 10px; align-items: center; font-size: 13px; color: var(--dim); margin-top: 9px; }
    .meters i { height: 8px; border-radius: 4px; background: #ffffff12; overflow: hidden; } .meters i b { display: block; height: 100%; border-radius: 4px; background: linear-gradient(90deg, #5147df, #8b7dff); }
    .meters em { font-style: normal; font-weight: 800; color: #fff; text-align: right; }
    .formula { font-size: 22px; font-weight: 800; padding: 16px; border-radius: 14px; background: #ffffff08; } .formula i { font-style: normal; color: #6f6d92; } .formula b { color: #6ee7b7; }
    .xl { display: flex; align-items: flex-end; gap: 16px; } .xls { padding: 10px 12px; border-radius: 10px; background: #1fa971; font-weight: 800; font-size: 12px; }
    .pairs { flex: 1; display: flex; align-items: flex-end; gap: 6px; height: 70px; } .pairs span { flex: 1; display: flex; gap: 2px; align-items: flex-end; height: 100%; }
    .pairs i { flex: 1; border-radius: 3px; background: #1fa971; } .pairs i + i { background: #e5484d99; }
    .flags div { display: flex; align-items: center; gap: 10px; font-size: 13px; font-weight: 700; padding: 9px 12px; border-radius: 10px; background: #ffffff08; margin-top: 7px; }
    .flags i { width: 10px; height: 10px; border-radius: 50%; } .flags .r { background: #e5484d; box-shadow: 0 0 10px #e5484d; } .flags .o { background: #f08a24; } .flags .y { background: #e8b100; }
    .bills { display: flex; gap: 10px; height: 90px; } .bills span { flex: 1; border-radius: 10px; background: repeating-linear-gradient(#fffdf7 0 12px, #ece5d2 12px 14px); transform: rotate(-4deg); box-shadow: 0 10px 20px #0006; }
    .bills span:nth-child(2) { transform: rotate(3deg) translateY(-6px); } .bills span:nth-child(3) { transform: rotate(-1deg); }
    .years { display: flex; align-items: center; gap: 14px; font-size: 20px; } .years b { padding: 10px 16px; border-radius: 12px; background: #ffffff0d; } .years b.nx { background: #5147df; } .years i { font-style: normal; color: var(--am); font-size: 26px; }
    .brandv { display: flex; align-items: center; gap: 14px; padding: 14px; border-radius: 16px; background: #fff; color: #1e2438; } .brandv strong { display: block; font-size: 17px; } .brandv small { color: #8d91a3; font-weight: 600; }

    /* tour */
    .screens { position: relative; margin: 56px auto 0; max-width: 1060px; perspective: 1800px; }
    .laptop { display: block; width: 86%; position: relative; transform: rotateY(8deg) rotateX(4deg); transform-style: preserve-3d; transition: transform .5s; }
    .laptop:hover { transform: rotateY(2deg) rotateX(1deg); }
    .lid { aspect-ratio: 16 / 9; border-radius: 18px 18px 6px 6px; padding: 12px; background: linear-gradient(#2a2840, #16152a); border: 1px solid #ffffff22; box-shadow: 0 50px 120px #000c, 0 0 100px #5147df40; }
    .lid iframe { width: 100%; height: 100%; border: 0; border-radius: 8px; background: #000; pointer-events: none; }
    .base { height: 16px; margin: 0 -4%; border-radius: 0 0 20px 20px; background: linear-gradient(#3a3858, #1b1a30); }
    .big-play { position: absolute; left: 50%; top: 45%; transform: translate(-50%, -50%); display: grid; place-items: center; width: 84px; height: 84px; border-radius: 50%;
      background: #ffffffee; color: var(--vi2); font-size: 26px; box-shadow: 0 0 0 14px #ffffff22, 0 20px 60px #000a; transition: transform .3s; }
    .laptop:hover .big-play { transform: translate(-50%, -50%) scale(1.1); }
    .phone-frame { position: absolute; right: 0; bottom: -40px; width: 22%; aspect-ratio: 9 / 19; padding: 9px; border-radius: 34px; background: #111322; border: 1px solid #ffffff2a;
      box-shadow: 0 40px 90px #000d, 0 0 80px #ffb54730; transform: rotateY(-14deg) rotateZ(2deg) translateZ(60px); }
    .phone-frame iframe { width: 100%; height: 100%; border: 0; border-radius: 26px; background: #000; pointer-events: none; }
    .chapters { display: flex; flex-wrap: wrap; justify-content: center; gap: 8px; margin-top: 80px; }
    .chapters span { padding: 8px 14px; border-radius: 999px; font-size: 13px; font-weight: 700; color: var(--dim); border: 1px solid var(--line); background: #ffffff05; }

    /* steps */
    .steps { display: grid; grid-template-columns: repeat(4, 1fr); gap: 18px; margin-top: 50px; text-align: left; counter-reset: s; }
    .step { position: relative; padding: 26px; border-radius: 22px; border: 1px solid var(--line); background: #ffffff05; }
    .num { display: grid; place-items: center; width: 44px; height: 44px; border-radius: 14px; font-weight: 800; font-size: 18px; background: linear-gradient(135deg, #8b7dff, #5147df); box-shadow: 0 10px 30px #5147df66; }
    .step h3 { font-size: 19px; margin: 18px 0 8px; letter-spacing: -.02em; } .step p { margin: 0; color: var(--dim); font-size: 14px; line-height: 1.6; }

    /* stats */
    .statband { display: grid; grid-template-columns: repeat(4, 1fr); max-width: 1200px; margin: 120px auto 0; border-block: 1px solid var(--line); }
    .lstat { padding: 44px 20px; text-align: center; border-right: 1px solid var(--line); } .lstat:last-child { border-right: 0; }
    .lstat b { display: block; font-size: clamp(40px, 5vw, 64px); letter-spacing: -.05em; background: linear-gradient(180deg, #fff, #8f8cb6); -webkit-background-clip: text; background-clip: text; color: transparent; }
    .lstat span { font-size: 14px; color: var(--dim); font-weight: 600; }

    /* security */
    .sec-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 18px; margin-top: 50px; text-align: left; }
    .sg { position: relative; overflow: hidden; padding: 26px; border-radius: 22px; border: 1px solid var(--line); background: #ffffff05; transform: perspective(900px) rotateX(var(--rx, 0deg)) rotateY(var(--ry, 0deg)); transition: transform .25s, border-color .25s; }
    .sgi { font-size: 28px; } .sg h3 { font-size: 19px; margin: 14px 0 8px; } .sg p { margin: 0; color: var(--dim); font-size: 14px; line-height: 1.6; }

    /* pricing */
    .prices { display: grid; grid-template-columns: repeat(3, 1fr); gap: 18px; margin-top: 50px; align-items: stretch; }
    .price { position: relative; overflow: hidden; padding: 32px 28px; border-radius: 26px; border: 1px solid var(--line); background: #ffffff06; text-align: left; display: flex; flex-direction: column; gap: 10px;
      transform: perspective(900px) rotateX(var(--rx, 0deg)) rotateY(var(--ry, 0deg)); transition: transform .25s, border-color .25s; }
    .price.pop { background: linear-gradient(160deg, #5147df40, #ffffff06 60%); border-color: #7b6cff77; box-shadow: 0 30px 90px #5147df33; }
    .badge { position: absolute; top: 18px; right: 18px; font-size: 11px; font-weight: 800; padding: 6px 12px; border-radius: 999px; background: var(--am); color: #3b2500; }
    .price small { font-size: 14px; font-weight: 800; color: var(--dim); text-transform: uppercase; letter-spacing: 2px; }
    .price h3 { font-size: 46px; letter-spacing: -.04em; margin: 0; } .price h3 span { font-size: 15px; color: var(--dim); font-weight: 600; letter-spacing: 0; margin-left: 4px; }
    .price p { margin: 0; color: var(--dim); font-size: 14px; flex: 1; }
    .free { font-size: 13px; font-weight: 800; color: #6ee7b7; }
    .pfeat { list-style: none; padding: 0; margin: 6px 0 4px; display: grid; gap: 8px; font-size: 14px; color: #d0cee8; }
    .pfeat li::before { content: '✓'; color: #6ee7b7; font-weight: 800; margin-right: 8px; } .price .btn { justify-content: center; margin-top: 8px; }
    .includes { list-style: none; padding: 0; margin: 40px auto 0; display: flex; flex-wrap: wrap; justify-content: center; gap: 10px 24px; max-width: 900px; color: var(--dim); font-size: 14px; font-weight: 600; }
    .includes li::before { content: '✓'; color: #6ee7b7; margin-right: 7px; font-weight: 800; }

    /* faq */
    .qa { max-width: 800px; margin: 40px auto 0; text-align: left; }
    details { border-bottom: 1px solid var(--line); }
    summary { list-style: none; cursor: pointer; display: flex; justify-content: space-between; gap: 20px; padding: 22px 4px; font-size: 18px; font-weight: 700; }
    summary::-webkit-details-marker { display: none; }
    summary i { font-style: normal; color: var(--vi); font-size: 24px; transition: transform .3s; } details[open] summary i { transform: rotate(45deg); }
    details p { margin: 0 4px 22px; color: var(--dim); line-height: 1.7; font-size: 15px; }

    /* final */
    .final { position: relative; text-align: center; padding: 160px 20px 140px; overflow: hidden; scroll-margin-top: 40px; }
    .final-glow { position: absolute; left: 50%; top: 50%; width: 900px; height: 900px; transform: translate(-50%, -50%); border-radius: 50%;
      background: radial-gradient(circle, #ffb54733, #5147df44 35%, transparent 65%); filter: blur(30px); animation: pulse 6s ease-in-out infinite; }
    @keyframes pulse { 50% { transform: translate(-50%, -50%) scale(1.12); } }
    .final > :not(.final-glow) { position: relative; }
    .final-logo { width: 96px; height: 96px; filter: drop-shadow(0 20px 50px #5147dfaa); animation: float 5s ease-in-out infinite; }
    .final h2 { font-size: clamp(40px, 6vw, 80px); margin-top: 28px; } .final p { color: var(--dim); font-size: 19px; margin: 0 0 34px; }
    .contact { display: flex; flex-wrap: wrap; justify-content: center; gap: 14px; margin-top: 40px; }
    .contact a { display: flex; align-items: center; gap: 12px; padding: 14px 20px; border-radius: 18px; text-align: left; background: #ffffff08; border: 1px solid var(--line); backdrop-filter: blur(10px); transition: border-color .2s, transform .2s; }
    .contact a:hover { border-color: #7b6cff77; transform: translateY(-2px); }
    .contact span { font-size: 24px; } .contact small { display: block; font-size: 12px; color: var(--dim); font-weight: 700; } .contact b { font-size: 15px; word-break: break-all; }
    .foot-contact a { color: #b7adff; } .foot-contact a:hover { color: #fff; }
    .foot { border-top: 1px solid var(--line); padding: 40px 20px; text-align: center; color: #6f6d92; }
    .foot .brand { justify-content: center; color: #fff; } .foot .foot-tag { color: #d9d5ff; font-weight: 700; letter-spacing: .04em; } .foot p { margin: 12px 0 6px; font-size: 14px; } .foot small { font-size: 12px; }

    /* modal */
    .tour-modal { position: fixed; inset: 0; z-index: 100; display: grid; place-items: center; padding: 16px; background: #02010acc; backdrop-filter: blur(10px); animation: fade .25s; }
    .modal-box { position: relative; width: min(1200px, calc(100vw - 32px), calc((100vh - 32px) * 16 / 9)); aspect-ratio: 16 / 9; border-radius: 18px; overflow: hidden; box-shadow: 0 40px 120px #000; border: 1px solid #ffffff22; }
    .modal-box iframe { width: 100%; height: 100%; border: 0; background: #000; }
    .close { position: absolute; top: 12px; right: 12px; z-index: 1; width: 40px; height: 40px; border-radius: 50%; background: #000a; color: #fff; font-size: 16px; }
    @keyframes fade { from { opacity: 0; } }

    /* reveal on scroll */
    .rv { opacity: 0; transform: translateY(40px); transition: opacity .9s cubic-bezier(.2,.8,.2,1), transform .9s cubic-bezier(.2,.8,.2,1); }
    .rv.in { opacity: 1; transform: none; }
    .tile.rv.in, .sg.rv.in, .price.rv.in { transform: perspective(900px) rotateX(var(--rx, 0deg)) rotateY(var(--ry, 0deg)); }

    /* responsive */
    @media (max-width: 1000px) {
      .nav nav { display: none; }
      .bento { grid-template-columns: 1fr 1fr; } .t-fees { grid-column: span 2; } .t-qr { grid-row: auto; grid-column: span 2; }
      .steps, .statband { grid-template-columns: 1fr 1fr; } .lstat:nth-child(2) { border-right: 0; } .lstat:nth-child(-n+2) { border-bottom: 1px solid var(--line); }
      .sec-grid, .pains { grid-template-columns: 1fr 1fr; }
      .float { display: none; }
    }
    @media (max-width: 680px) {
      .ghost { display: none; } .nav-cta .btn { padding: 10px 14px; font-size: 13px; }
      .hero { padding-top: 120px; min-height: 0; }
      .orb { opacity: .4; } .br-d { display: none; }
      .dash-body { grid-template-columns: 1fr; } aside { display: none; } .dstats { grid-template-columns: 1fr 1fr; } .dash-row { grid-template-columns: 1fr; } .live { display: none; }
      .bento, .sec-grid, .pains, .steps, .prices { grid-template-columns: 1fr; } .t-fees, .t-qr { grid-column: auto; } .t-fees { grid-template-columns: 1fr; }
      .rcpt { transform: none; }
      .sec { padding-top: 90px; }
      .laptop { width: 100%; transform: none; } .phone-frame { display: none; } .chapters { margin-top: 30px; }
      .statband { grid-template-columns: 1fr 1fr; }
      .big-play { width: 64px; height: 64px; }
    }
    @media (prefers-reduced-motion: reduce) {
      .aurora, .float, .track, .scan, .final-glow, .final-logo, .bars span { animation: none !important; }
      .rv { opacity: 1; transform: none; transition: none; }
      .stage { transform: none; }
    }
  `,
})
export class LandingComponent implements AfterViewInit, OnDestroy {
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly zone = inject(NgZone);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly api = inject(Api);

  protected readonly contactUrl = CONTACT_URL;
  protected readonly contact = CONTACT;
  protected readonly year = new Date().getFullYear();
  protected readonly scrolled = signal(false);
  protected readonly tourOpen = signal(false);
  protected readonly tourEmbed: SafeResourceUrl = this.sanitizer.bypassSecurityTrustResourceUrl('media/ojas-demo.html?embed=1');
  protected readonly adEmbed: SafeResourceUrl = this.sanitizer.bypassSecurityTrustResourceUrl('media/ojas-ad.html?embed=1');

  protected readonly nav = ['Dashboard', 'Students', 'Admissions', 'Fees', 'Faculty', 'Tests', 'Expenses', 'Reports', 'Activity log'];
  protected readonly bars = [42, 55, 48, 70, 64, 82, 76, 96];
  protected readonly running = [['Mr. Iyer', 'Physics · Class 11', '00:40:12'], ['Ms. Khan', 'Biology · Class 12', '00:21:03'], ['Mr. Rao', 'Maths · Class 9', '00:05:47']];
  protected readonly audiences = ['JEE & NEET coaching', 'Tuition classes', 'Computer institutes', 'Music & dance schools', 'Language academies', 'Sports academies', 'Skill training centres', 'Art & design studios'];
  protected readonly pains = [
    ['📒', 'Fee registers', 'Who paid, who didn’t, and which receipt book was it in? Hours lost every month.'],
    ['📊', 'Excel sheets', 'One wrong formula and the totals quietly stop adding up. Nobody knows who changed what.'],
    ['💬', 'WhatsApp groups', 'Attendance by message, reminders by hand, and important things buried in chats.'],
  ];
  protected readonly marks = [['Physics', 86], ['Chemistry', 78], ['Biology', 92]] as const;
  protected readonly pairBars = [[60, 30], [75, 40], [55, 35], [90, 45], [70, 30], [95, 50]];
  protected readonly qr = buildQr();
  protected readonly chapters = ['Sign-in & roles', 'Dashboard', 'Students', 'Admissions', 'Fees', 'Faculty pay', 'Faculty portal', 'Student portal', 'Tests', 'Expenses', 'Reports', 'Activity log', 'New year', 'Your brand'];
  protected readonly steps = [
    ['We create your academy', 'Your own private workspace, academy code and Super Admin login — on day one.'],
    ['We bring in your data', 'Send us your student and fee lists in Excel. We help you load them.'],
    ['Your team learns it in 30 min', 'Staff, accountant and teachers each see only what their job needs.'],
    ['Run everything from one app', 'Fees, attendance, admissions and reports — on any phone or computer.'],
  ];
  protected readonly stats = [['6', 'roles, each with the right access'], ['20 s', 'QR code refresh — no proxy attendance'], ['0', 'payments ever deleted — only voided'], ['100%', 'private to your academy']];
  protected readonly security = [
    ['🏛️', 'A private workspace', 'Each academy signs in with its own code and only ever sees its own records.'],
    ['🔑', 'Roles that fit', 'Super Admin, Admin, Accountant and Staff each see only what their job needs.'],
    ['🧾', 'Voids, not deletes', 'Money entries are never edited away. Mistakes are voided with a reason, so totals always reconcile.'],
    ['🕵️', 'Full history', 'Every change is logged — who did what, and when — with flags for anything risky.'],
    ['🔒', 'Account lockout', 'Five wrong passwords lock an account for 15 minutes. New logins must set their own password.'],
    ['📱', 'Signed QR codes', 'Check-in codes are signed and expire in a minute, so a photo of one is useless later.'],
  ];
  /** The price list, from the plans the owner manages (Platform → Plans), so the website never drifts from real prices. */
  private readonly publicPlans = signal<PublicPlan[]>([]);
  protected readonly plansFailed = signal(false);
  protected readonly plans = computed(() => this.publicPlans().map(p => ({
    name: p.name,
    free: p.price === 0,
    highlighted: p.highlighted,
    price: money(p.price),
    per: p.price === 0 ? '/forever' : PER[p.interval],
    forWho: p.description,
    yearly: p.yearlyPrice != null
      ? `or ${money(p.yearlyPrice)} a year${p.monthsFree > 0 ? ` — ${p.monthsFree} month${p.monthsFree === 1 ? '' : 's'} free` : ''}`
      : '',
    features: features(p),
  })));
  protected readonly freePlan = computed(() => this.publicPlans().find(p => p.price === 0) ?? null);
  /** Cheapest paid plan, as a monthly price. */
  private readonly cheapest = computed(() =>
    this.publicPlans().filter(p => p.price > 0).sort((a, b) => a.price / MONTHS[a.interval] - b.price / MONTHS[b.interval])[0] ?? null);
  protected readonly pricePill = computed(() => {
    const paid = this.cheapest();
    if (!paid) return this.freePlan() ? 'Free for small classes' : '';
    const from = `plans from ${money(paid.price)}${PER[paid.interval].replace('/', ' a ')}`;
    return this.freePlan() ? `Free for small classes · paid ${from}` : from.charAt(0).toUpperCase() + from.slice(1);
  });

  protected readonly includes = ['Fees & receipts', 'Faculty attendance & pay', 'Online admissions', 'Student & teacher portals', 'Tests & report cards', 'Expenses with bills', 'Excel reports', 'Activity alerts', 'Emails in your academy’s name'];
  protected readonly faqs = computed(() => {
    const free = this.freePlan();
    const paid = this.cheapest();
    const yearly = paid?.yearlyPrice != null ? `, or ${money(paid.yearlyPrice)} a year` : '';
    const list: string[][] = [
      ['Do I need to install anything?', 'No. Ojas runs in the browser on any phone, tablet or computer. Teachers and students simply sign in with your academy code.'],
    ];
    if (free) {
      const limits = [limit(free.maxStudents, 'student'), limit(free.maxFaculty, 'faculty', 'faculty'),
        free.maxReceiptEmailsPerMonth == null ? 'unlimited emailed receipts' : `${free.maxReceiptEmailsPerMonth} emailed receipts a month`];
      list.push(['Is it really free?', `Yes. The ${free.name} plan has no time limit: ${joinAnd(limits)}. It never runs out; you upgrade only if you need more.`]);
    }
    list.push(['What happens if I go over a limit?',
      `Nothing is deleted. You keep everything you already have and simply cannot add more until you upgrade${paid ? ` — from ${money(paid.price)}${PER[paid.interval].replace('/', ' a ')}${yearly}` : ''}.`]);
    return [...list, ...FIXED_FAQS];
  });

  private raf = 0;
  private readonly cleanups: (() => void)[] = [];

  protected go(id: string): void {
    const el = id === 'top' ? this.host.nativeElement : this.host.nativeElement.querySelector(`#${id}`);
    el?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  protected start(): void {
    if (this.contactUrl) window.open(this.contactUrl, '_blank', 'noopener');
    else this.go('start');
  }

  protected openTour(): void { this.tourOpen.set(true); }

  constructor() {
    this.api.get<PublicPlan[]>('public/plans').subscribe({
      next: plans => this.publicPlans.set(plans),
      error: () => this.plansFailed.set(true),
    });
  }

  ngAfterViewInit(): void {
    const root = this.host.nativeElement as HTMLElement;
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    // Reveal-on-scroll.
    const io = new IntersectionObserver(entries => entries.forEach(e => { if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target); } }), { threshold: 0.15 });
    root.querySelectorAll('.rv').forEach(el => io.observe(el));
    this.cleanups.push(() => io.disconnect());

    this.zone.runOutsideAngular(() => {
      // Scroll: nav background and the 3D stage flattening as it comes into view.
      const stage = root.querySelector<HTMLElement>('.stage');
      const onScroll = () => {
        const y = window.scrollY;
        if ((y > 30) !== this.scrolled()) this.zone.run(() => this.scrolled.set(y > 30));
        if (stage) stage.style.setProperty('--sp', String(Math.min(1, y / (window.innerHeight * 0.6))));
      };
      window.addEventListener('scroll', onScroll, { passive: true });
      onScroll();
      this.cleanups.push(() => window.removeEventListener('scroll', onScroll));

      // Pointer: tilt + spotlight on cards, slight turn of the stage.
      let mouseX = 0, mouseY = 0;
      const onMove = (e: PointerEvent) => {
        mouseX = e.clientX / window.innerWidth - 0.5; mouseY = e.clientY / window.innerHeight - 0.5;
        if (stage && !reduced) stage.style.setProperty('--tx', mouseX.toFixed(3));
        const card = (e.target as HTMLElement).closest<HTMLElement>('.tilt');
        root.querySelectorAll<HTMLElement>('.tilt.hot').forEach(c => { if (c !== card) { c.classList.remove('hot'); c.style.setProperty('--rx', '0deg'); c.style.setProperty('--ry', '0deg'); } });
        if (!card || reduced) return;
        const r = card.getBoundingClientRect(), px = (e.clientX - r.left) / r.width, py = (e.clientY - r.top) / r.height;
        card.classList.add('hot');
        card.style.setProperty('--ry', `${(px - 0.5) * 8}deg`); card.style.setProperty('--rx', `${(0.5 - py) * 8}deg`);
        card.style.setProperty('--mx', `${px * 100}%`); card.style.setProperty('--my', `${py * 100}%`);
      };
      window.addEventListener('pointermove', onMove, { passive: true });
      this.cleanups.push(() => window.removeEventListener('pointermove', onMove));

      // The orb: a rotating sphere of particles with a warm core, behind the hero.
      const canvas = root.querySelector<HTMLCanvasElement>('canvas.orb');
      const ctx = canvas?.getContext('2d');
      if (!canvas || !ctx) return;
      const N = 1100, pts: [number, number, number][] = [];
      for (let i = 0; i < N; i++) {
        const y = 1 - (i / (N - 1)) * 2, rad = Math.sqrt(1 - y * y), th = Math.PI * (3 - Math.sqrt(5)) * i;
        pts.push([Math.cos(th) * rad, y, Math.sin(th) * rad]);
      }
      const stars = Array.from({ length: 140 }, () => [Math.random(), Math.random(), Math.random() * 1.2 + 0.2, Math.random() * Math.PI * 2]);
      let w = 0, h = 0, dpr = 1, visible = true;
      const resize = () => { dpr = Math.min(2, window.devicePixelRatio || 1); w = canvas.clientWidth; h = canvas.clientHeight; canvas.width = w * dpr; canvas.height = h * dpr; };
      resize();
      window.addEventListener('resize', resize);
      this.cleanups.push(() => window.removeEventListener('resize', resize));
      const vis = new IntersectionObserver(([e]) => { visible = e.isIntersecting; if (visible && !reduced) loop(); });
      vis.observe(canvas);
      this.cleanups.push(() => vis.disconnect());
      let rotY = 0, rotX = -0.35, tmx = 0, tmy = 0;
      const draw = (t: number) => {
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        ctx.clearRect(0, 0, w, h);
        for (const [sx, sy, sr, ph] of stars) {
          ctx.globalAlpha = 0.25 + 0.35 * Math.sin(t / 900 + ph);
          ctx.fillStyle = '#c9c4ff'; ctx.beginPath(); ctx.arc(sx * w, sy * h, sr, 0, Math.PI * 2); ctx.fill();
        }
        ctx.globalAlpha = 1;
        tmx += (mouseX - tmx) * 0.04; tmy += (mouseY - tmy) * 0.04;
        rotY = t / 9000 + tmx * 0.8; const rx = rotX + tmy * 0.4;
        const cx = w / 2, cy = Math.min(h * 0.36, 360), R = Math.min(w * 0.36, 380);
        const core = ctx.createRadialGradient(cx, cy, 0, cx, cy, R * 1.25);
        core.addColorStop(0, 'rgba(255,181,71,0.28)'); core.addColorStop(0.35, 'rgba(106,92,242,0.20)'); core.addColorStop(1, 'rgba(5,4,15,0)');
        ctx.fillStyle = core; ctx.fillRect(0, 0, w, h);
        const cyR = Math.cos(rotY), syR = Math.sin(rotY), cxR = Math.cos(rx), sxR = Math.sin(rx);
        for (let i = 0; i < N; i++) {
          let [x, y, z] = pts[i];
          const wob = 1 + 0.03 * Math.sin(t / 700 + i * 0.37);
          x *= wob; y *= wob; z *= wob;
          const x1 = x * cyR - z * syR, z1 = x * syR + z * cyR;
          const y2 = y * cxR - z1 * sxR, z2 = y * sxR + z1 * cxR;
          const persp = 2.4 / (2.4 + z2), px = cx + x1 * R * persp, py = cy + y2 * R * persp;
          const depth = (z2 + 1) / 2;
          ctx.globalAlpha = 0.22 + (1 - depth) * 0.78;
          ctx.fillStyle = depth < 0.35 ? '#ffd08a' : depth < 0.6 ? '#b7adff' : '#7b6cff';
          const s = (1 - depth) * 2.4 + 0.7;
          ctx.fillRect(px - s / 2, py - s / 2, s, s);
        }
        // orbit ring
        ctx.globalAlpha = 1; ctx.strokeStyle = 'rgba(183,173,255,0.18)'; ctx.lineWidth = 1;
        ctx.beginPath(); ctx.ellipse(cx, cy, R * 1.45, R * 0.32, -0.25 + tmx * 0.2, 0, Math.PI * 2); ctx.stroke();
        const a = t / 2200;
        const ox = cx + Math.cos(a) * R * 1.45 * Math.cos(-0.25) - Math.sin(a) * R * 0.32 * Math.sin(-0.25);
        const oy = cy + Math.cos(a) * R * 1.45 * Math.sin(-0.25) + Math.sin(a) * R * 0.32 * Math.cos(-0.25);
        const sg = ctx.createRadialGradient(ox, oy, 0, ox, oy, 18); sg.addColorStop(0, 'rgba(255,181,71,1)'); sg.addColorStop(1, 'rgba(255,181,71,0)');
        ctx.fillStyle = sg; ctx.beginPath(); ctx.arc(ox, oy, 18, 0, Math.PI * 2); ctx.fill();
      };
      const loop = () => {
        cancelAnimationFrame(this.raf);
        const tick = (t: number) => { if (!visible) return; draw(t); this.raf = requestAnimationFrame(tick); };
        this.raf = requestAnimationFrame(tick);
      };
      if (reduced) draw(0); else loop();
    });
  }

  ngOnDestroy(): void {
    cancelAnimationFrame(this.raf);
    this.cleanups.forEach(fn => fn());
  }
}

/** A 21×21 QR-looking pattern (decorative), with the three corner markers. */
function buildQr(): boolean[] {
  const n = 21, cells: boolean[] = [];
  let seed = 7;
  const rnd = () => { seed = (seed * 16807) % 2147483647; return seed / 2147483647; };
  const finder = (i: number, j: number, fi: number, fj: number) => {
    const x = i - fi, y = j - fj;
    if (x < 0 || y < 0 || x > 6 || y > 6) return null;
    return x === 0 || y === 0 || x === 6 || y === 6 || (x >= 2 && x <= 4 && y >= 2 && y <= 4);
  };
  for (let j = 0; j < n; j++) for (let i = 0; i < n; i++) {
    const f = finder(i, j, 0, 0) ?? finder(i, j, n - 7, 0) ?? finder(i, j, 0, n - 7);
    const nearFinder = (i < 8 && j < 8) || (i > n - 9 && j < 8) || (i < 8 && j > n - 9);
    cells.push(f ?? (nearFinder ? false : rnd() > 0.5));
  }
  return cells;
}
