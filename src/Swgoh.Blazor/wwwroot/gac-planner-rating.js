(() => {
    const percentPattern = /([0-9]+(?:[.,][0-9]+)?)\s*%/;
    const samplesPattern = /([0-9]+)\s+muestras?/i;

    const parsePercent = (element) => {
        if (!element) return null;
        const match = element.textContent?.match(percentPattern);
        if (!match) return null;
        const value = Number.parseFloat(match[1].replace(',', '.'));
        return Number.isFinite(value) ? value : null;
    };

    const parseSamples = (element) => {
        if (!element) return null;
        const match = element.textContent?.match(samplesPattern);
        if (!match) return null;
        const value = Number.parseInt(match[1], 10);
        return Number.isFinite(value) ? value : null;
    };

    const findStat = (card, suffix) =>
        [...card.querySelectorAll('.planner-hint-stats span')]
            .find(item => item.textContent?.toLowerCase().includes(suffix));

    const riskFrom = (winRate, oneShotRate, samples) => {
        if (winRate === null) {
            return { tone: 'unknown', label: 'Sin evaluar', caption: 'Sin histórico suficiente' };
        }

        let level;
        if (winRate >= 85 && (oneShotRate === null || oneShotRate >= 70)) {
            level = 0;
        } else if (winRate >= 70 && (oneShotRate === null || oneShotRate >= 50)) {
            level = 1;
        } else {
            level = 2;
        }

        // Tiny samples should never look safer than the evidence supports.
        if (samples !== null && samples < 10 && level < 2) {
            level += 1;
        }

        return level === 0
            ? { tone: 'low', label: 'Riesgo bajo', caption: 'Counter favorable' }
            : level === 1
                ? { tone: 'medium', label: 'Riesgo medio', caption: 'Revisa mods y datacron' }
                : { tone: 'high', label: 'Riesgo alto', caption: 'Ataque delicado' };
    };

    const metric = (label, value, modifier = '') => {
        const item = document.createElement('span');
        item.className = `planner-rating-metric ${modifier}`.trim();

        const strong = document.createElement('strong');
        strong.textContent = value;
        item.append(strong);

        const small = document.createElement('small');
        small.textContent = label;
        item.append(small);
        return item;
    };

    const decorateCard = (card) => {
        const header = card.querySelector(':scope > .planner-enemy-card-top');
        if (!header) return;

        const existing = header.querySelector(':scope > .planner-matchup-rating');
        existing?.remove();

        const winElement = findStat(card, 'win');
        const oneShotElement = findStat(card, 'one-shot');
        const samplesElement = findStat(card, 'muestras');
        const winRate = parsePercent(winElement);
        const oneShotRate = parsePercent(oneShotElement);
        const samples = parseSamples(samplesElement);

        const defeated = card.classList.contains('planner-enemy-defeated');
        const risk = defeated
            ? { tone: 'complete', label: 'Completada', caption: 'Defensa derrotada' }
            : riskFrom(winRate, oneShotRate, samples);

        const rating = document.createElement('aside');
        rating.className = `planner-matchup-rating planner-risk-${risk.tone}`;
        rating.setAttribute('aria-label', 'Valoración rápida del enfrentamiento');

        const title = document.createElement('div');
        title.className = 'planner-rating-title';
        const kicker = document.createElement('span');
        kicker.textContent = 'Lectura rápida';
        const riskLabel = document.createElement('strong');
        riskLabel.textContent = risk.label;
        const caption = document.createElement('small');
        caption.textContent = risk.caption;
        title.append(kicker, riskLabel, caption);
        rating.append(title);

        const metrics = document.createElement('div');
        metrics.className = 'planner-rating-metrics';
        metrics.append(
            metric('win', winRate === null ? '—' : `${winRate.toFixed(winRate % 1 === 0 ? 0 : 1)}%`, 'planner-rating-win'),
            metric('one-shot', oneShotRate === null ? '—' : `${oneShotRate.toFixed(oneShotRate % 1 === 0 ? 0 : 1)}%`, 'planner-rating-oneshot'),
            metric('muestras', samples === null ? '—' : samples.toLocaleString('es-ES'), 'planner-rating-samples')
        );
        rating.append(metrics);

        header.append(rating);
    };

    const decorate = () => {
        document.querySelectorAll('.planner-enemy-card').forEach(decorateCard);
    };

    let scheduled = false;
    const scheduleDecorate = () => {
        if (scheduled) return;
        scheduled = true;
        requestAnimationFrame(() => {
            scheduled = false;
            decorate();
        });
    };

    document.addEventListener('DOMContentLoaded', scheduleDecorate, { once: true });
    window.addEventListener('load', scheduleDecorate, { once: true });

    const observer = new MutationObserver(mutations => {
        const relevant = mutations.some(mutation =>
            [...mutation.addedNodes].some(node =>
                node.nodeType === Node.ELEMENT_NODE &&
                (node.matches?.('.planner-enemy-card, .planner-hint-stats') ||
                 node.querySelector?.('.planner-enemy-card, .planner-hint-stats'))));

        if (relevant) scheduleDecorate();
    });

    observer.observe(document.body, { childList: true, subtree: true });
    scheduleDecorate();
})();