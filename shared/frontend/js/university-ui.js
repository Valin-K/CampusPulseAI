window.UniversityUI = (() => {
    const toast = (message, duration = 3200) => {
        const existing = document.querySelector('.ui-toast');
        existing?.remove();

        const element = document.createElement('div');
        element.className = 'ui-toast';
        element.setAttribute('role', 'status');
        element.textContent = message;
        document.body.appendChild(element);
        window.setTimeout(() => element.remove(), duration);
    };

    const reveal = (id) => {
        window.requestAnimationFrame(() => {
            const element = document.getElementById(id);
            if (!element) return;

            element.classList.remove('reveal-pulse');
            void element.offsetWidth;
            element.classList.add('reveal-pulse');
            element.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            element.focus({ preventScroll: true });
        });
    };

    const setText = (id, value) => {
        const element = document.getElementById(id);
        if (element) element.textContent = value ?? '';
    };

    const openModal = (id) => {
        const modal = document.getElementById(id);
        if (!modal) return;
        modal.hidden = false;
        modal.querySelector('input, select, button')?.focus();
        document.body.style.overflow = 'hidden';
    };

    const closeModal = (id) => {
        const modal = document.getElementById(id);
        if (modal) modal.hidden = true;
        document.body.style.overflow = '';
    };

    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape') return;
        document.querySelectorAll('[data-university-modal]:not([hidden])').forEach(modal => closeModal(modal.id));
    });

    return { toast, reveal, setText, openModal, closeModal };
})();
