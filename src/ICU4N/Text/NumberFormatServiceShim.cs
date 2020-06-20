﻿using ICU4N.Globalization;
using ICU4N.Impl;
using ICU4N.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using static ICU4N.Impl.ICULocaleService;

namespace ICU4N.Text
{
    internal class NumberFormatServiceShim : NumberFormat.NumberFormatShim
    {
        internal override CultureInfo[] GetAvailableLocales() // ICU4N TODO: API - Rename GetCultures() and add CultureTypes enum
        {
            if (service.IsDefault)
            {
                return ICUResourceBundle.GetAvailableLocales();
            }
            return service.GetAvailableLocales();
        }

        internal override UCultureInfo[] GetAvailableULocales() // ICU4N TODO: API - Rename GetUCultures() and add CultureTypes enum
        {
            if (service.IsDefault)
            {
                return ICUResourceBundle.GetAvailableUCultures();
            }
            return service.GetUCultures();
        }

        private sealed class NFFactory : LocaleKeyFactory
        {
            private NumberFormatFactory @delegate;

            internal NFFactory(NumberFormatFactory @delegate)
            : base(@delegate.Visible ? Visible : Invisible)
            {

                this.@delegate = @delegate;
            }

            public override object Create(ICUServiceKey key, ICUService srvc)
            {
                if (!HandlesKey(key) || !(key is LocaleKey))
                {
                    return null;
                }

                LocaleKey lkey = (LocaleKey)key;
                object result = @delegate.CreateFormat(lkey.GetCanonicalCulture(), lkey.Kind);
                if (result == null)
                {
                    result = srvc.GetKey(key, null, this);
                }
                return result;
            }

            protected override ICollection<string> GetSupportedIDs()
            {
                return @delegate.GetSupportedLocaleNames();
            }
        }

        internal override object RegisterFactory(NumberFormatFactory factory)
        {
            return service.RegisterFactory(new NFFactory(factory));
        }

        internal override bool Unregister(object registryKey)
        {
            return service.UnregisterFactory((IServiceFactory)registryKey);
        }

        internal override NumberFormat CreateInstance(UCultureInfo desiredLocale, NumberFormatStyle choice)
        {

            // use service cache
            //          if (service.isDefault()) {
            //              return NumberFormat.createInstance(desiredLocale, choice);
            //          }

            UCultureInfo[] actualLoc = new UCultureInfo[1];
            NumberFormat fmt = (NumberFormat)service.Get(desiredLocale, (int)choice,
                                                         actualLoc);
            if (fmt == null)
            {
                throw new MissingManifestResourceException("Unable to construct NumberFormat");
            }
            fmt = (NumberFormat)fmt.Clone();

            // ICU4N TODO: Currency
            //// If we are creating a currency type formatter, then we may have to set the currency
            //// explicitly, since the actualLoc may be different than the desiredLocale        
            //if (choice == NumberFormat.CURRENCYSTYLE ||
            //     choice == NumberFormat.ISOCURRENCYSTYLE ||
            //     choice == NumberFormat.PLURALCURRENCYSTYLE)
            //{
            //    fmt.SetCurrency(Currency.GetInstance(desiredLocale));
            //}

            UCultureInfo uloc = actualLoc[0];
            fmt.SetCulture(uloc, uloc); // services make no distinction between actual & valid
            return fmt;
        }

        internal class RBNumberFormatFactory : ICUResourceBundleFactory
        {
            protected override object HandleCreate(UCultureInfo loc, int kind, ICUService srvc)
            {
                return NumberFormat.CreateInstance(loc, kind);
            }
        }

        private class NFService : ICULocaleService
        {
            internal NFService()
                : base("NumberFormat")
            {
                this.RegisterFactory(new RBNumberFormatFactory());
                MarkDefault();
            }
        }
        private static ICULocaleService service = new NFService();
    }
}
