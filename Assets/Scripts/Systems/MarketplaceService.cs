using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudCode;

namespace TCGCollector.Systems
{
    /// <summary>One active marketplace listing, as returned by MarketplaceService.GetListingsAsync.</summary>
    [Serializable]
    public class MarketplaceListing
    {
        public string listingId;
        public string sellerId;
        public string cardId;
        public int price;
        public long createdAt;
    }

    /// <summary>Thrown for any marketplace failure the Cloud Code scripts reject on purpose (not
    /// enough currency, listing already gone, buying your own card, ...). .Message is clean
    /// French text, safe to show directly in the UI.</summary>
    public class MarketplaceException : Exception
    {
        public MarketplaceException(string message) : base(message) { }
    }

    /// <summary>
    /// Client-side wrapper around the 4 marketplace Cloud Code scripts (getListings/listCard/
    /// buyListing/cancelListing - published from the Unity Dashboard, not part of this project on
    /// disk).
    ///
    /// Unlike the rest of the save system, marketplace calls are server-authoritative: normal
    /// currency/card changes only affect the owning player, so they stay client-trusted and sync
    /// lazily via CloudSyncService. A trade moves currency/cards between two different players, so
    /// it must be validated and applied server-side - a client can't be trusted to honestly debit
    /// a buyer and credit a seller.
    ///
    /// After any successful mutation, pulls the caller's own currency/ownedCards back down via
    /// CloudSyncService.RefreshCurrencyAndCardsAsync so CollectionManager reflects the change
    /// immediately.
    /// </summary>
    public class MarketplaceService
    {
        private const string GetListingsFunction = "marketplace_getListings";
        private const string ListCardFunction = "marketplace_listCard";
        private const string BuyListingFunction = "marketplace_buyListing";
        private const string CancelListingFunction = "marketplace_cancelListing";

        [Serializable] private class GetListingsResult { public List<MarketplaceListing> listings; }
        [Serializable] private class ListCardResult { public string listingId; public string cardId; public int price; }
        [Serializable] private class BuyListingResult { public string cardId; public int price; public int sellerEarned; }
        [Serializable] private class CancelListingResult { public string cardId; }

        /// <summary>Every active listing across every seller, in no particular order - the UI
        /// sorts/filters as needed.</summary>
        public async Task<List<MarketplaceListing>> GetListingsAsync()
        {
            await EnsureSignedInAsync();
            try
            {
                var result = await CloudCodeService.Instance.CallEndpointAsync<GetListingsResult>(
                    GetListingsFunction, new Dictionary<string, object>());
                return result?.listings ?? new List<MarketplaceListing>();
            }
            catch (CloudCodeException e)
            {
                throw new MarketplaceException(CleanMessage(e));
            }
        }

        /// <summary>Lists one copy of cardId for sale at the given price (po); the card is removed
        /// from the caller's collection server-side (escrow). Throws MarketplaceException on
        /// failure with nothing changed. Returns the new listing's id on success.</summary>
        public async Task<string> ListCardAsync(string cardId, int price)
        {
            await EnsureSignedInAsync();
            await PushLocalStateAsync();
            try
            {
                var args = new Dictionary<string, object> { { "cardId", cardId }, { "price", price } };
                var result = await CloudCodeService.Instance.CallEndpointAsync<ListCardResult>(ListCardFunction, args);
                await RefreshLocalStateAsync();
                return result?.listingId;
            }
            catch (CloudCodeException e)
            {
                throw new MarketplaceException(CleanMessage(e));
            }
        }

        /// <summary>Buys an active listing - debits the caller, credits the seller (minus
        /// commission), and adds the card to the caller's collection, all server-side. Throws
        /// MarketplaceException on failure with nothing changed.</summary>
        public async Task BuyListingAsync(string listingId)
        {
            await EnsureSignedInAsync();
            await PushLocalStateAsync();
            try
            {
                var args = new Dictionary<string, object> { { "listingId", listingId } };
                await CloudCodeService.Instance.CallEndpointAsync<BuyListingResult>(BuyListingFunction, args);
                await RefreshLocalStateAsync();
            }
            catch (CloudCodeException e)
            {
                throw new MarketplaceException(CleanMessage(e));
            }
        }

        /// <summary>Cancels a listing the caller owns - the card goes back to their collection, no
        /// commission charged. Throws MarketplaceException on failure.</summary>
        public async Task CancelListingAsync(string listingId)
        {
            await EnsureSignedInAsync();
            await PushLocalStateAsync();
            try
            {
                var args = new Dictionary<string, object> { { "listingId", listingId } };
                await CloudCodeService.Instance.CallEndpointAsync<CancelListingResult>(CancelListingFunction, args);
                await RefreshLocalStateAsync();
            }
            catch (CloudCodeException e)
            {
                throw new MarketplaceException(CleanMessage(e));
            }
        }

        private static async Task EnsureSignedInAsync()
        {
            if (CloudSyncService.Instance != null)
                await CloudSyncService.Instance.EnsureSignedInAsync();
        }

        /// <summary>Pushes this client's current currency/ownedCards up to the cloud right before a
        /// marketplace call. Necessary because CloudSyncService otherwise only pushes at app start,
        /// so a card/purchase earned earlier this session wouldn't be visible yet to a Cloud Code
        /// script checking server-side ownership/balance.</summary>
        private static async Task PushLocalStateAsync()
        {
            if (CloudSyncService.Instance != null)
                await CloudSyncService.Instance.PushNowAsync();
        }

        private static async Task RefreshLocalStateAsync()
        {
            if (CloudSyncService.Instance != null)
                await CloudSyncService.Instance.RefreshCurrencyAndCardsAsync();
        }

        /// <summary>Cloud Code relays our scripts' thrown Error messages back through
        /// CloudCodeException.Message - already clean French text, safe to show in the UI. Falls
        /// back to a generic message if it's empty.</summary>
        private static string CleanMessage(CloudCodeException e)
        {
            return !string.IsNullOrEmpty(e.Message) ? e.Message : "Erreur inconnue de la marketplace.";
        }
    }
}
