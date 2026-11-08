# Vibe Coding Portfolio

Real, production websites built through AI-assisted ("vibe") coding: describing intent in plain language, previewing every change, and refining iteratively until it ships.

**By [Musbaudeen Oyedeji](https://github.com/Musbaudo)**

---

## Projects

### 1. NyotaCore: company website
**Live:** https://nyotacore.com.ng  ·  _Code coming soon_

Multi-page marketing site for a Lagos-based technology company (software, cloud/DevOps, cybersecurity, AI agents, vessel data services).

**Highlights**
- Animated hero constellation and "Power Business" gradient headline
- Services mega-menu, interactive "What We Do" card carousel with photo-on-hover
- "Why NyotaCore" fade-swap tabs and a 9-sector Industries orbit
- Services page with a two-column hover-driven list/detail panel and case study
- Site-wide SEO: meta tags, Open Graph images, JSON-LD structured data, sitemap, robots.txt
- CI/CD: GitHub Actions on a self-hosted Windows runner deploying to IIS (staging → production)

**Stack:** HTML · PHP · Tailwind CSS v4 · JavaScript · IIS · GitHub Actions

---

### 2. NyotaCore Talent: recruitment platform
**Live:** https://talent.nyotacore.com.ng  ·  **Folder:** [`nyotacore-talent/`](./nyotacore-talent)

Recruitment site connecting skilled African talent with employers in Nigeria and abroad, covering skilled trades (mechanics, welders, electricians) and tech roles (Cloud, DevOps, SRE, AI, developers).

**Highlights**
- Sticky header with scroll-spy navigation and a mobile menu
- Employer / candidate toggle on a single lead form, with CV upload
- PHP/MySQL backend that creates and seeds its own schema on first run
- Password-protected admin area: lead pipeline and status tracking, notes, CV downloads, CSV export
- Roles/locations catalogue editor and team management with role-based access (admin vs recruiter)
- SMTP email notifications, SEO and share images

**Stack:** PHP · MySQL · JavaScript · HTML/CSS · cPanel hosting

**Run locally**
```bash
cd nyotacore-talent
cp private/config.example.php private/config.php   # fill in your own DB + SMTP settings
php -S localhost:8000
```

---

## How these were built

1. **Describe** the goal and design intent (Figma specs, screen recordings, reference sites)
2. **Preview** each change visually before it touches the codebase
3. **Refine** in tight loops: precise feedback, new versioned patch each time
4. **Ship** through scripted installers and CI/CD to staging, then production

## Note
Secrets (database and SMTP credentials, server deploy scripts) are intentionally excluded. Copy `nyotacore-talent/private/config.example.php` to `config.php` and add your own settings.
