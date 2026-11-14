// Back-office : petites aides d'affichage sur téléphone et tablette, sans toucher aux pages.
(() => {
    // 1. Tableaux : chaque cellule reçoit le nom de sa colonne (data-label). Sur petit écran,
    //    le CSS présente alors chaque ligne comme une fiche « Colonne : valeur » au lieu d'un tableau à faire défiler.
    const label = table => {
        const heads = [...table.querySelectorAll(':scope > thead th')].map(th => th.innerText.trim());
        if (!heads.length) return;
        // Plus de 2 colonnes (hors tableaux de saisie) : présenté en fiches sur téléphone.
        const cards = heads.length > 2 && !table.matches('.quote-lines, .plan-mix');
        if (table.classList.contains('as-cards') !== cards) table.classList.toggle('as-cards', cards);
        if (!table.hasAttribute('data-labelled')) table.setAttribute('data-labelled', '');
        for (const row of table.querySelectorAll(':scope > tbody > tr')) {
            let col = 0;
            for (const cell of row.children) {
                const text = (cell.colSpan || 1) > 1 ? '' : (heads[col] || '');
                if (cell.getAttribute('data-label') !== text) cell.setAttribute('data-label', text);
                col += cell.colSpan || 1;
            }
        }
    };
    const labelAll = () => document.querySelectorAll('table.admin-table').forEach(label);

    // 2. Menu replié : il se referme quand on choisit une page (la mise en page reste affichée entre les pages).
    document.addEventListener('click', e => {
        if (e.target.closest('.admin-sidebar nav a')) {
            const toggle = document.getElementById('admin-menu');
            if (toggle) toggle.checked = false;
        }
    });

    let pending = false;
    new MutationObserver(() => {
        if (pending) return;
        pending = true;
        requestAnimationFrame(() => { pending = false; labelAll(); });
    }).observe(document.body, { childList: true, subtree: true, characterData: true });
    labelAll();
})();
