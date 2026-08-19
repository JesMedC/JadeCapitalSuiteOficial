import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { CookieConsentBannerComponent } from '@shared/cookie-consent/cookie-consent.component';

// ============================================================================
//  App shell — Wave 11 slice 11.4.
//
//  The cookie banner lives at the ROOT component (not per-route) so it
//  survives navigation between landing / auth / trader subtrees. The
//  banner reads its own state via CookieConsentService.shouldShowBanner
//  and renders nothing once the user has chosen a tier.
// ============================================================================

@Component({
  selector: 'jcs-root',
  standalone: true,
  imports: [RouterOutlet, CookieConsentBannerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <router-outlet></router-outlet>
    <jcs-cookie-consent-banner></jcs-cookie-consent-banner>
  `,
})
export class App {}
