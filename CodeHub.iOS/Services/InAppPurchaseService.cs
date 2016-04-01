﻿using System;
using StoreKit;
using System.Threading.Tasks;
using Foundation;
using System.Collections.Generic;
using System.Reactive.Subjects;
using CodeHub.Core.Services;

namespace CodeHub.iOS.Services
{
    public interface IInAppPurchaseService
    {
        Task<SKProductsResponse> RequestProductData(params string[] productIds);

        Task Restore();

        Task PurchaseProduct(string productId);

        IObservable<Exception> ThrownExceptions { get; }
    }

    public class InAppPurchaseService : IInAppPurchaseService
    {
        private readonly TransactionObserver _observer;
        private TaskCompletionSource<bool> _actionSource;
        private readonly LinkedList<object> _productDataRequests = new LinkedList<object>();
        private readonly ISubject<Exception> _errorSubject = new Subject<Exception>();
        private readonly IDefaultValueService _defaultValueService;

        public IObservable<Exception> ThrownExceptions { get { return _errorSubject; } }

        private void OnPurchaseError(SKPayment id, Exception e)
        {
            _actionSource?.TrySetException(e);
        }

        private void OnPurchaseSuccess(SKPayment id)
        {
            _actionSource?.TrySetResult(true);
        }

        public InAppPurchaseService(IDefaultValueService defaultValueService)
        {
            _defaultValueService = defaultValueService;
            _observer = new TransactionObserver(this);
            SKPaymentQueue.DefaultQueue.AddTransactionObserver(_observer);
        }

        public async Task<SKProductsResponse> RequestProductData (params string[] productIds)
        {
            var array = new NSString[productIds.Length];
            for (var i = 0; i < productIds.Length; i++)
                array[i] = new NSString(productIds[i]);

            var tcs = new TaskCompletionSource<SKProductsResponse>();
            _productDataRequests.AddLast(tcs);

            try
            {
                var productIdentifiers = NSSet.MakeNSObjectSet<NSString>(array); //NSSet.MakeNSObjectSet<NSString>(array);​​​
                var productsRequest = new SKProductsRequest(productIdentifiers);
                productsRequest.ReceivedResponse += (sender, e) => tcs.SetResult(e.Response);
                productsRequest.RequestFailed += (sender, e) => tcs.SetException(new Exception(e.Error.LocalizedDescription));
                productsRequest.Start();
                if (await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30))) != tcs.Task)
                    throw new InvalidOperationException("Timeout waiting for Apple to respond");
                var ret = tcs.Task.Result;
                productsRequest.Dispose();
                return ret;
            }
            finally
            {
                _productDataRequests.Remove(tcs);
            }
        }

        public static bool CanMakePayments()
        {
            return SKPaymentQueue.CanMakePayments;        
        }

        public Task Restore()
        {
            _actionSource = new TaskCompletionSource<bool>();
            SKPaymentQueue.DefaultQueue.RestoreCompletedTransactions();
            return _actionSource.Task;
        }

        public async Task PurchaseProduct(string productId)
        {
            _actionSource = new TaskCompletionSource<bool>();
            var payment = SKMutablePayment.PaymentWithProduct(productId);
            SKPaymentQueue.DefaultQueue.AddPayment (payment);
            await _actionSource.Task;
        }

        private void CompleteTransaction (SKPaymentTransaction transaction)
        {
            var productId = transaction.Payment.ProductIdentifier;
            _defaultValueService.Set(productId, true);
            OnPurchaseSuccess(transaction.Payment);
        }

        private void RestoreTransaction (SKPaymentTransaction transaction)
        {
            var productId = transaction.OriginalTransaction.Payment.ProductIdentifier;;
            _defaultValueService.Set(productId, true);
            OnPurchaseSuccess(transaction.OriginalTransaction.Payment);
        }

        private void FailedTransaction (SKPaymentTransaction transaction)
        {
            var errorString = transaction.Error != null ? transaction.Error.LocalizedDescription : "Unable to process transaction!";
            OnPurchaseError(transaction.Payment, new Exception(errorString));
        }

        private void DeferedTransaction(SKPaymentTransaction transaction)
        {
            const string msgString = "Parental controls are active. After approval, the purchase will be complete.";
            OnPurchaseError(transaction.Payment, new Exception(msgString));
        }

        private class TransactionObserver : SKPaymentTransactionObserver
        {
            private readonly InAppPurchaseService _inAppPurchases;

            public TransactionObserver(InAppPurchaseService inAppPurchases)
            {
                _inAppPurchases = inAppPurchases;
            }

            public override void UpdatedTransactions(SKPaymentQueue queue, SKPaymentTransaction[] transactions)
            {
                foreach (SKPaymentTransaction transaction in transactions)
                {
                    try
                    {

                        switch (transaction.TransactionState)
                        {
                            case SKPaymentTransactionState.Purchased:
                                _inAppPurchases.CompleteTransaction(transaction);
                                SKPaymentQueue.DefaultQueue.FinishTransaction(transaction);
                                break;
                            case SKPaymentTransactionState.Failed:
                                _inAppPurchases.FailedTransaction(transaction);
                                SKPaymentQueue.DefaultQueue.FinishTransaction(transaction);
                                break;
                            case SKPaymentTransactionState.Restored:
                                _inAppPurchases.RestoreTransaction(transaction);
                                SKPaymentQueue.DefaultQueue.FinishTransaction(transaction);
                                break;
                            case SKPaymentTransactionState.Deferred:
                                _inAppPurchases.DeferedTransaction(transaction);
                                SKPaymentQueue.DefaultQueue.FinishTransaction(transaction);
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        _inAppPurchases._errorSubject.OnNext(e);
                    }
                }
            }

            public override void RestoreCompletedTransactionsFailedWithError (SKPaymentQueue queue, NSError error)
            {
                if (error.Code != 2)
                {
                    _inAppPurchases._actionSource?.TrySetException(new Exception(error.LocalizedDescription));
                }
            }
        }
    }

    public static class SKProductExtension 
    {
        public static string LocalizedPrice (this SKProduct product)
        {
            var formatter = new NSNumberFormatter ();
            formatter.FormatterBehavior = NSNumberFormatterBehavior.Version_10_4;  
            formatter.NumberStyle = NSNumberFormatterStyle.Currency;
            formatter.Locale = product.PriceLocale;
            var formattedString = formatter.StringFromNumber(product.Price);
            return formattedString;
        }
    }
}

