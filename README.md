# Vibe Coding Portfolio

Real, production websites built through AI-assisted ("vibe") coding: describing intent in plain language, previewing every change, and refining iteratively until it ships.

**By [Musbaudeen Oyedeji](https://github.com/Musbaudo)**

---

## Projects

### 1. Tech Company Website
**Live:** https://nyotacore.com.ng  Â·  **Folder:** [`tech-company-website/`](./tech-company-website)

Multi-page marketing site for a Lagos-based technology company (software, cloud/DevOps, cybersecurity, AI agents, vessel data services).

**Highlights**
- Animated hero constellation and "Power Business" gradient headline
- Services mega-menu, interactive "What We Do" card carousel with photo-on-hover
- "Why NyotaCore" fade-swap tabs and a 9-sector Industries orbit
- Services page with a two-column hover-driven list/detail panel and case study
- Site-wide SEO: meta tags, Open Graph images, JSON-LD structured data, sitemap, robots.txt
- CI/CD: GitHub Actions on a self-hosted Windows runner deploying to IIS (staging â†’ production)

**Stack:** HTML Â· PHP Â· Tailwind CSS v4 Â· JavaScript Â· IIS Â· GitHub Actions

---

### 2. Recruitment Platform
**Live:** https://talent.nyotacore.com.ng  Â·  **Folder:** [`recruitment-platform/`](./recruitment-platform)

Recruitment site connecting skilled African talent with employers in Nigeria and abroad, covering skilled trades (mechanics, welders, electricians) and tech roles (Cloud, DevOps, SRE, AI, developers).

**Highlights**
- Sticky header with scroll-spy navigation and a mobile menu
- Employer / candidate toggle on a single lead form, with CV upload
- PHP/MySQL backend that creates and seeds its own schema on first run
- Password-protected admin area: lead pipeline and status tracking, notes, CV downloads, CSV export
- Roles/locations catalogue editor and team management with role-based access (admin vs recruiter)
- SMTP email notifications, SEO and share images

**Stack:** PHP Â· MySQL Â· JavaScript Â· HTML/CSS Â· cPanel hosting

**Run locally**
```bash
cd recruitment-platform
cp private/config.example.php private/config.php   # fill in your own DB + SMTP settings
php -S localhost:8000
```

---

### 3. NGO Charity Website
**Live:** https://mission.nyotacore.com.ng  Â·  **Folder:** [`ngo-charity-website/`](./ngo-charity-website)

Charity website for a foundation supporting displaced families: story-led pages, online giving and a contact form.

**Highlights**
- Home, About, Donate and Contact pages with a warm, editorial design (Fraunces + Inter)
- Online donations through Paystack inline checkout, with server-side transaction verification
- SMTP contact form via PHPMailer, with validation and friendly status messages
- Secrets kept in a web-blocked `private/config.php`, never in HTML/JS

**Stack:** HTML Â· CSS Â· JavaScript Â· PHP Â· Paystack Â· PHPMailer Â· cPanel hosting

**Run locally**
```bash
cd ngo-charity-website
cp private/config.example.php private/config.php   # add SMTP + Paystack test keys
php -S localhost:8000
```

---

### 4. SpeakSafe
**Folder:** [`speaksafe/`](./speaksafe)

ASP.NET web application.

**Stack:** C# · ASP.NET · Razor · SQL Server

**Run locally**
```bash
cd speaksafe
# set your connection string in appsettings.json (values are CHANGE_ME in the repo)
dotnet run
```

---

## How these were built

1. **Describe** the goal and design intent (Figma specs, screen recordings, reference sites)
2. **Preview** each change visually before it touches the codebase
3. **Refine** in tight loops: precise feedback, new versioned patch each time
4. **Ship** through scripted installers and CI/CD to staging, then production

## Note
Secrets (database and SMTP credentials, server deploy scripts) are intentionally excluded. In each project, copy `private/config.example.php` to `private/config.php` and add your own settings.



