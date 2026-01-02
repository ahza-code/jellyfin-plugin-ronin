(function() {
    'use strict';
    console.log('--- [ RONIN ]: Script Start (Type-Check Mode) ---');

    let lastProcessedItemId = null;
    let mutationTimeout = null;
    const MUTATION_DEBOUNCE_MS = 50;
    const cfg = window.RoninVariables || {};

    // --- Helper Functions ---
    function getItemIdFromUrl() {
        const hash = window.location.hash;
        const match = hash.match(/#\/details\?id=([a-fA-F0-9]+)/);
        return match ? match[1] : null;
    }

    const fillerTags = ["Manga Canon", "Mixed Canon/Filler", "Filler", "Anime Canon"];

    function getMatchingTag(tags) {
        if (!tags || tags.length === 0) return null;
        return tags.find(tag => fillerTags.includes(tag)) || null;
    }

    function injectBadge(targetElement, tag) {
        const tagClass = tag
            .toLowerCase()
            .replace(/[\s/]+/g, '-')
            .trim();
        const badge = document.createElement('div');
        badge.className = `mediaInfoItem anime-tag-badge ${tagClass}`;
        // Optional color class
        if (cfg.RONIN_ENABLE_BADGE_COLORS === true) {
            badge.classList.add('colored');
        }
        badge.textContent = tag;
            
        if (targetElement) {
            targetElement.appendChild(badge);
            console.log('[ RONIN ]: Appended badge...');
        } else {
            console.log('[ RONIN ]: Failed to target element for injection...');
        }
    }
    
    // --- API & Processors ---

    async function fetchItemData(itemId) {
        if (!window.ApiClient) {
            console.warn('[ RONIN ]: ApiClient not ready yet.');
            return null;
        }
        const userId = ApiClient.getCurrentUserId();
        const url = `/Users/${userId}/Items/${itemId}`;
        try {
            return await ApiClient.getJSON(url);
        } catch (error) { return null; }
    }

    // Logic for when the URL ID is a SEASON (or Series)
    async function processSeasonView(seasonId) {
        
        const episodeElements = document.querySelectorAll('.listItem[data-type="Episode"]');
        if (episodeElements.length === 0) return; // DOM not ready yet, observer will retry

        console.log('[ RONIN ]: Type is Season/Series. Processing list...');
        lastProcessedItemId = seasonId;
        
        const userId = ApiClient.getCurrentUserId();
        const url = `/Shows/${seasonId}/Episodes?seasonId=${seasonId}&userId=${userId}&Fields=Tags`; 
        
        try {
            const response = await ApiClient.getJSON(url);
            const episodes = response.Items || [];
            
            const episodeTagMap = episodes.reduce((map, episode) => {
                const tag = getMatchingTag(episode.Tags);
                if (tag) map[episode.Id] = tag;
                return map;
            }, {});

            if (Object.keys(episodeTagMap).length === 0) {
                console.log('[ RONIN ]: No episodes matched tags — skipping injection.');
                return;
            }
            
            episodeElements.forEach(el => {
                const episodeId = el.getAttribute('data-id');
                const episodeTag = episodeTagMap[episodeId];
                if (episodeTag) {
                    const target = el.querySelector('.secondary.listItemMediaInfo.listItemBodyText');
                    const hasBadgeAlready = target.querySelector('.anime-tag-badge');
                    if (target && !hasBadgeAlready) injectBadge(target, episodeTag);
                }

            });
        } catch (error) { console.error('Season fetch failed', error); }
    }

    // Logic for when the URL ID is an EPISODE
    function processEpisodeView(itemId, itemData) {

        const miscInfoElements = document.querySelectorAll('.itemMiscInfo-primary');
        if (miscInfoElements.length === 0) return; // DOM not ready yet, observer will retry

        console.log('[ RONIN ]: Type is Episode. Injecting into header...');
        lastProcessedItemId = itemId;

        const matchingTag = getMatchingTag(itemData.Tags);
        if (matchingTag) {
            miscInfoElements.forEach(el => {
                const hasBadgeAlready = el.querySelector('.anime-tag-badge');
                if (el && !hasBadgeAlready) injectBadge(el, matchingTag);
            });
        }
    }

    // --- Main Dispatcher ---
    async function processPage() {
        const itemId = getItemIdFromUrl();
        const existingBadges = document.querySelectorAll('.anime-tag-badge');

        if (!itemId) return;
        if (itemId === lastProcessedItemId && existingBadges.length > 0) return;

        // 1. Fetch the Item Details to know WHAT we are looking at (Season vs Episode)
        const itemData = await fetchItemData(itemId);
        
        if (!itemData) return;

        if (existingBadges.length > 0) {
            existingBadges.forEach(el => {
                el.remove();
            });
        }

        // 2. Route based on "Type"
        if (itemData.Type === 'Episode') {
            // Honor config: skip if disabled
            if (!cfg.RONIN_SHOW_EPISODE_BADGES) {
                lastProcessedItemId = itemId;
                return;
            }
            processEpisodeView(itemId, itemData);
        } 
        else if (itemData.Type === 'Season') {
            if (!cfg.RONIN_SHOW_SEASON_LIST_BADGES) {
                lastProcessedItemId = itemId;
                return;
            }
            await processSeasonView(itemId);
        } else {
            // Skip any other kind of content
            lastProcessedItemId = itemId;
        }
    }

    // --- Initialization ---
    function initializeObserver() {
        processPage(); 
        window.addEventListener('hashchange', () => {
            lastProcessedItemId = null; // Reset on navigation
            setTimeout(processPage, 50);
        });
        
        const observer = new MutationObserver(() => {
            clearTimeout(mutationTimeout);
            mutationTimeout = setTimeout(() => {
                const currentId = getItemIdFromUrl();
                if (currentId) processPage();
            }, MUTATION_DEBOUNCE_MS);
        });

        observer.observe(document.body, { childList: true, subtree: true });
    }

    const waitForJellyfin = setInterval(() => {
        if (window.ApiClient && window.ApiClient.getCurrentUserId) {
            clearInterval(waitForJellyfin);
            console.log('[ RONIN ]: Jellyfin Core Loaded. Initializing.');
            
            if (document.readyState === 'loading') {
                document.addEventListener('DOMContentLoaded', initializeObserver);
            } else {
                initializeObserver();
            }
        }
    }, 500); // Check every 500ms
})();