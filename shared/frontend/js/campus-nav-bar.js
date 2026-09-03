class CampusNavBar extends HTMLElement {
    static observedAttributes = [
        'page-name',
        'section-label',
        'home-href',
        'portal-href',
        'user-name',
        'user-details',
        'user-initials',
        'service-count',
        'context-label'
    ];

    constructor() {
        super();
        this.attachShadow({ mode: 'open' });
    }

    connectedCallback() {
        this.render();
    }

    attributeChangedCallback() {
        if (this.isConnected) this.render();
    }

    render() {
        const value = (name, fallback = '') => this.getAttribute(name) || fallback;
        const escape = (text) => text.replace(/[&<>"']/g, character => ({
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            '"': '&quot;',
            "'": '&#39;'
        })[character]);

        const pageName = escape(value('page-name', 'CampusPulse'));
        const sectionLabel = escape(value('section-label', 'Student services'));
        const homeHref = escape(value('home-href', '/'));
        const portalHref = value('portal-href');
        const userName = value('user-name');
        const userDetails = escape(value('user-details'));
        const userInitials = escape(value('user-initials', 'ST'));
        const serviceCount = value('service-count');
        const contextLabel = escape(value('context-label', 'Student portal'));

        const globalLink = portalHref
            ? `<nav class="global-nav" aria-label="Application navigation"><a href="${escape(portalHref)}">All services</a></nav>`
            : '';
        const fallbackUser = userName
            ? `<div class="user" aria-label="Current student"><span class="avatar" aria-hidden="true">${userInitials}</span><span><strong>${escape(userName)}</strong><small>${userDetails}</small></span></div>`
            : '';
        const user = `<slot name="user-selector">${fallbackUser}</slot>`;
        const count = serviceCount
            ? `<span class="service-count">${escape(serviceCount)}</span>`
            : '';

        this.shadowRoot.innerHTML = `
            <style>
                :host { display: block; font-family: Inter, Aptos, "Segoe UI", sans-serif; }
                * { box-sizing: border-box; }
                .topbar { min-height: 5.25rem; padding: .85rem clamp(1rem, 4vw, 4rem); display: flex; align-items: center; gap: 1.15rem; color: #fff; background: #0b263d; border-bottom: 3px solid #f4b942; box-shadow: 0 10px 30px rgba(9, 30, 50, .18); }
                .brand { display: flex; align-items: center; gap: .7rem; color: inherit; text-decoration: none; }
                .mark { width: 2.65rem; height: 2.65rem; display: block; flex: 0 0 auto; filter: drop-shadow(0 4px 8px rgba(0, 0, 0, .16)); }
                .brand-copy, .page-name, .user > span:last-child { display: grid; gap: .1rem; }
                .brand-copy strong { font-size: 1.05rem; letter-spacing: -.02em; }
                .brand-copy small, .user small { color: #b9d1e2; font-size: .7rem; }
                .divider { width: 1px; height: 2.5rem; background: rgba(255, 255, 255, .2); }
                .page-name { margin-right: auto; }
                .page-name span { color: #8fc4e2; font-size: .67rem; font-weight: 800; letter-spacing: .12em; text-transform: uppercase; }
                .page-name strong { font-size: 1rem; }
                .global-nav { display: flex; align-items: center; }
                .global-nav a { padding: .62rem .8rem; border-radius: .55rem; color: #dcebf4; font-size: .8rem; font-weight: 750; text-decoration: none; }
                .global-nav a:hover { color: #fff; background: rgba(255, 255, 255, .1); }
                .service-count { padding: .6rem .8rem; border: 1px solid rgba(255, 255, 255, .18); border-radius: .65rem; color: #dcebf4; background: rgba(255, 255, 255, .07); font-size: .78rem; font-weight: 750; }
                .user { display: flex; align-items: center; gap: .65rem; padding-left: .9rem; border-left: 1px solid rgba(255, 255, 255, .18); }
                .user strong { font-size: .8rem; }
                .avatar { width: 2.35rem; height: 2.35rem; display: grid; place-items: center; border: 1px solid rgba(255, 255, 255, .22); border-radius: 50%; color: #fff; background: #1769aa; font-size: .72rem; font-weight: 850; }
                ::slotted([slot="user-selector"]) { display: block; min-width: min(19rem, 32vw); padding-left: .9rem; border-left: 1px solid rgba(255, 255, 255, .18); }
                .context-bar { padding: .65rem clamp(1rem, 4vw, 4rem); color: #243b53; background: rgba(255, 255, 255, .72); border-bottom: 1px solid #d9e2ec; font-size: .72rem; font-weight: 700; }
                .context-bar span { display: flex; align-items: center; gap: .45rem; }
                .context-bar i { width: .48rem; height: .48rem; border-radius: 50%; background: #20a47a; box-shadow: 0 0 0 4px rgba(32, 164, 122, .12); }
                @media (max-width: 760px) {
                    .topbar { flex-wrap: wrap; gap: .75rem; }
                    .divider { display: none; }
                    .page-name { order: 3; flex-basis: 100%; padding-top: .65rem; border-top: 1px solid rgba(255, 255, 255, .15); }
                    .global-nav { margin-left: auto; }
                    .user > span:last-child { display: none; }
                    .user { padding-left: 0; border-left: 0; }
                    ::slotted([slot="user-selector"]) { width: 100%; min-width: 0; padding: .65rem 0 0; border-top: 1px solid rgba(255, 255, 255, .15); border-left: 0; }
                }
                @media (max-width: 480px) {
                    .brand-copy { display: none; }
                    .global-nav a { padding-inline: .55rem; }
                    .service-count { margin-left: auto; }
                }
            </style>
            <header class="topbar">
                <a class="brand" href="${homeHref}" aria-label="CampusPulse home">
                    <svg class="mark" viewBox="0 0 64 64" aria-hidden="true">
                        <rect width="64" height="64" rx="16" fill="#f4b942"></rect>
                        <path fill="#0b263d" d="M10 25 32 12l22 13v5H10v-5Zm5 24v-5h4V32h7v12h3V32h6v12h3V32h7v12h4v5H15Z"></path>
                        <path d="M7 38h12l4-6 5 12 6-17 6 11h17" fill="none" stroke="#fff" stroke-width="3.5" stroke-linecap="round" stroke-linejoin="round"></path>
                        <path d="M12 53h40" fill="none" stroke="#1769aa" stroke-width="3" stroke-linecap="round"></path>
                    </svg>
                    <span class="brand-copy"><strong>CampusPulse</strong><small>University services</small></span>
                </a>
                <span class="divider" aria-hidden="true"></span>
                <div class="page-name"><span>${sectionLabel}</span><strong>${pageName}</strong></div>
                ${count}
                ${globalLink}
                ${user}
            </header>
            <div class="context-bar"><span><i aria-hidden="true"></i>${contextLabel}</span></div>`;
    }
}

if (!customElements.get('campus-nav-bar')) {
    customElements.define('campus-nav-bar', CampusNavBar);
}
