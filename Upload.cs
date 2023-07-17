using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WikiCamps.Common.Models;
using WikiCamps.Entities;
using WikiCamps.Entities.Request;
using WikiCamps.Models.Core;
using WikiCamps.Models.Databases;
using WikiCamps.Models.Sites;
using WikiCamps.Services;
using WikiCamps.Views.MainMenu;
using Xamarin.Forms;

namespace WikiCamps.Models.Wiki
{
    public static class OfflineContentUpdater
    {
        public static bool WifiOnly { get; private set; }
        public static bool Updating { get; private set; }
        public static Region CurrentRegion { get; private set; }

        private static SemaphoreSlim _semaphoreObject = new SemaphoreSlim(3, 3);
        private static int batchSize = 5;

        public static void SetWifiOnly(bool wifiOnly, bool save)
        {
            WifiOnly = wifiOnly;
            if (save)
            {
                Settings.Set("wifiOnly", WifiOnly ? 1 : 0);
                Settings.Commit();
            }
        }
        private static void FetchComments(Region region) //gets comments
        {
            Task.Run(async () =>
            {
                bool success = true;
                var commentLastPage = Settings.GetInt($"commentPage" + region.Id, 0);
                bool hasMorePage = true;

                while (region.Offline && success && hasMorePage)
                {
                    success = false;
                    try
                    {
                        var commentTasks = new List<Task<MarkerCommentListEntity>>();
                        for (int i = 1; i <= batchSize; i++)
                        {
                            var currentPage = commentLastPage + i;
                            var request = new MarkerCommentListRequestEntity(PostDataEx.GetUserBasePostDataRequestEntity())
                            {
                                Page = currentPage,
                                RegionId = region.Id
                            };
                            var localRequestEntity = request; //to avoid race condition
                            commentTasks.Add(Task.Run(() => ServiceFactory.ExceptionHandler.HandlerRequestTaskAsync(() => ServiceFactory.MarkerCommentManager.GetListAsync(localRequestEntity)))); //this gets the data from server
                        }
                        var commentList = await Task.WhenAll(commentTasks);
                        
                        foreach (var comments in commentList)
                        {
                            if (comments == null) continue;
                            if (comments.Comments.Any())
                            {
                                Database.Accounts.Open(() =>
                                {
                                    Database.Comments.Open(() =>
                                    {
                                        var parallelOptions = new ParallelOptions()
                                        {
                                            MaxDegreeOfParallelism = Environment.ProcessorCount
                                        };

                                        Parallel.ForEach(comments.Comments, parallelOptions, comment =>
                                        {
                                            var account = new Account(comment.User);
                                            Database.Accounts.InsertOrReplace(account);

                                            var currentComment = Database.Comments.Select<SiteComment>("ContentId=? AND MarkerId=?", comment.Id, comment.MarkerId).FirstOrDefault();

                                            if (currentComment == null)
                                            {
                                                currentComment = new SiteComment();
                                                currentComment.Id = 0;
                                                currentComment.ContentId = comment.Id;
                                                currentComment.MarkerId = comment.MarkerId;
                                            }

                                            //currentComment.Hide = hidden;
                                            currentComment.UserId = account.UserId;
                                            currentComment.User = account;
                                            currentComment.Time = comment.DateCreated.HasValue ? Convert.ToInt32(comment.DateCreated.Value.ToUnixTimeSeconds()) : 0;
                                            currentComment.Ups = comment.Ups;
                                            currentComment.Downs = comment.Downs;
                                            currentComment.Text = comment.Comment;

                                            if (currentComment.Id == 0)
                                            {
                                                Database.Comments.Insert(currentComment);
                                            }
                                            else
                                            {
                                                Database.Comments.Update(currentComment);
                                            }
                                        });
                                    });
                                });
                            }
                            hasMorePage = comments.HasMorePages;
                            SiteContentView.AddToRegionCount(region, comments.Comments.Count, 0, 0);
                        }

                        success = true;
                        commentLastPage = commentList.ToList().LastOrDefault().CurrentPage;
                        Settings.Set("commentPage" + region.Id, commentLastPage);
                        Settings.Commit();
                    }
                    catch
                    {
                    }
                }
                _semaphoreObject.Release();
            });
        }

        private static void FetchPrices(Region region) //gets pricess
        {
            Task.Run(async () =>
            {
                bool success = true;
                var priceLastPage = Settings.GetInt($"pricePage" + region.Id, 0);
                bool hasMorePage = true;

                while (region.Offline && success && hasMorePage)
                {
                    success = false;

                    try
                    {
                        var priceTasks = new List<Task<MarkerPriceListEntity>>();
                        for (int i = 1; i <= batchSize; i++)
                        {
                            var currentPage = priceLastPage + i;
                            var request = new MarkerPriceListRequestEntity(PostDataEx.GetUserBasePostDataRequestEntity())
                            {
                                Page = currentPage,
                                RegionId = region.Id
                            };
                            var localRequestEntity = request; //to avoid race condition
                            priceTasks.Add(Task.Run(() => ServiceFactory.ExceptionHandler.HandlerRequestTaskAsync(() => ServiceFactory.MarkerPriceManager.GetListAsync(localRequestEntity)))); //this gets the data from server
                        }
                        var priceList = await Task.WhenAll(priceTasks);

                        foreach(var prices in priceList)
                        {
                            if(prices == null) continue;
                            if (prices.Prices.Any())
                            {
                                Database.Accounts.Open(() =>
                                {
                                    Database.Prices.Open(() =>
                                    {
                                        var parallelOptions = new ParallelOptions()
                                        {
                                            MaxDegreeOfParallelism = Environment.ProcessorCount
                                        };

                                        Parallel.ForEach(prices.Prices, parallelOptions, price =>
                                        {
                                            var account = new Account(price.User);
                                            Database.Accounts.InsertOrReplace(account);

                                            var currentPrice = Database.Prices.Select<SitePrice>("ContentId=? AND MarkerId=?", price.Id, price.MarkerId).FirstOrDefault();

                                            if (currentPrice == null)
                                            {
                                                currentPrice = new SitePrice();
                                                currentPrice.Id = 0;
                                                currentPrice.ContentId = price.Id.ToString();
                                                currentPrice.MarkerId = price.MarkerId;
                                            }

                                            //currentPrice.Hide = hidden;
                                            currentPrice.UserId = account.UserId;
                                            currentPrice.User = account;
                                            //currentPhoto.Admin = account.IsAdmin;
                                            currentPrice.Time = price.DateCreated.HasValue ? Convert.ToInt32(price.DateCreated.Value.ToUnixTimeSeconds()) : 0;
                                            currentPrice.Ups = price.Ups;
                                            currentPrice.Downs = price.Downs;
                                            currentPrice.Text = price.Name;
                                            currentPrice.Value = price.Price;
                                            currentPrice.CurrencyId = price.CurrencyId;
                                            currentPrice.ItemTypeId = price.MarkerItemTypeId;

                                            if (currentPrice.Id == 0)
                                            {
                                                Database.Prices.Insert(currentPrice);
                                            }
                                            else
                                            {
                                                Database.Prices.Update(currentPrice);
                                            }
                                        });
                                    });
                                });
                            }
                            hasMorePage = prices.HasMorePages;
                            SiteContentView.AddToRegionCount(region, 0, prices.Prices.Count, 0);
                        }

                        success = true;
                        priceLastPage = priceList.ToList().LastOrDefault().CurrentPage;
                        Settings.Set("pricePage" + region.Id, priceLastPage);
                        Settings.Commit();
                    }
                    catch
                    {
                    }
                }
                _semaphoreObject.Release();
            });
        }

        private static void FetchPhotos(Region region) //gets photos
        {
            Task.Run(async () =>
            {
                bool success = true;
                int skippedPhotoCount = 0;
                var photoLastPage = Settings.GetInt($"photoPage" + region.Id, 0);
                bool hasMorePage = true;

                while (region.Offline && success && hasMorePage)
                {
                    success = false;                    
                    try
                    {
                        var photoTasks = new List<Task<MarkerPhotoListEntity>>();
                        for (int i = 1; i <= batchSize; i++)
                        {
                            var currentPage = photoLastPage + i;
                            var request = new MarkerPhotoListRequestEntity(PostDataEx.GetUserBasePostDataRequestEntity())
                            {
                                Page = currentPage,
                                RegionId = region.Id
                            };
                            var localRequestEntity = request; //to avoid race condition
                            photoTasks.Add(Task.Run(() => ServiceFactory.ExceptionHandler.HandlerRequestTaskAsync(() => ServiceFactory.MarkerPhotoManager.GetListAsync(localRequestEntity)))); //this gets the data from server
                        }
                        var photoList = await Task.WhenAll(photoTasks);

                        var savePhotoTasks = new List<Task>();
                        foreach(var photoSet in photoList)
                        {
                            skippedPhotoCount = 0;
                            if (photoSet != null)
                            {
                                SavePhotoToDb(photoSet, region, out skippedPhotoCount);
                                hasMorePage = photoSet.HasMorePages;
                                SiteContentView.AddToRegionCount(region, 0, 0, photoSet.Photos.Count - skippedPhotoCount);
                            }
                        }
                        await Task.WhenAll(savePhotoTasks);
                        success = true;
                        photoLastPage = photoList.ToList().LastOrDefault().CurrentPage;
                        Settings.Set("photoPage" + region.Id, photoLastPage);
                        Settings.Commit();
                    }
                    catch
                    {
                    }
                }
                _semaphoreObject.Release();
            });
        }

        public static void Update(Action<bool> callback) // This is called when the sync button is tapped
        {
            if (Updating)
            {
                callback?.Invoke(true);
                return;
            }
            Updating = true;

            Task.Run(() =>
            {
                foreach (Region region in Region.List)
                {
                    CurrentRegion = region;
                    if (!region.Offline) continue;

                    _semaphoreObject.Wait();
                    FetchComments(region);
                    _semaphoreObject.Wait();
                    FetchPrices(region);
                    _semaphoreObject.Wait();
                    FetchPhotos(region);

                    while (_semaphoreObject.CurrentCount != 3) {
                        // loop until all semaphoreSlim items free up
                    }

                    region.SyncTime = Time.NowUTC;
                    Settings.Set("newSyncTime" + region.Id, region.SyncTime);

                    SiteContentView.RefreshIfSynced(region);
                }
                CurrentRegion = null;

                Device.BeginInvokeOnMainThread(() =>
                {
                    Updating = false;
                    callback?.Invoke(true);
                });
            });
        }

        private static void SavePhotoToDb(MarkerPhotoListEntity photoSet, Region region, out int skippedPhotoCount)
        {
            int skipped = 0;
            if (photoSet.Photos == null)
            {
                Database.Accounts.Open(() =>
                {
                    Database.Photos.Open(() =>
                    {
                        var parallelOptions = new ParallelOptions()
                        {
                            MaxDegreeOfParallelism = Environment.ProcessorCount
                        };
                        Parallel.ForEach(photoSet.Photos, parallelOptions, photo =>
                        {
                            if (photo.Details == null)
                            {
                                skipped++;
                                return;
                            }

                            var account = new Account(photo.User);
                            Database.Accounts.InsertOrReplace(account);
                            var filename = photo.Details.PhotoLocation.Split(new char[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).Last();

                            var currentPhoto = Database.Photos.Select<SitePhoto>("ContentId=? AND MarkerId=?", photo.Id, photo.MarkerId).FirstOrDefault();

                            if (currentPhoto == null)
                            {
                                currentPhoto = new SitePhoto();
                                currentPhoto.Id = 0;
                                currentPhoto.ContentId = photo.Id;
                                currentPhoto.MarkerId = photo.MarkerId;
                            }

                            //currentPhoto.Hide = hidden;
                            currentPhoto.UserId = account.UserId;
                            currentPhoto.User = account;
                            //currentPhoto.Admin = account.IsAdmin;
                            currentPhoto.Time = photo.DateCreated.HasValue ? Convert.ToInt32(photo.DateCreated.Value.ToUnixTimeSeconds()) : 0;
                            currentPhoto.Ups = photo.Ups;
                            currentPhoto.Downs = photo.Downs;
                            currentPhoto.File = filename;
                            currentPhoto.Text = photo.Description;
                            currentPhoto.ThumbnailLocation = photo.Details.ThumbnailLocation;
                            currentPhoto.PhotoLocation = photo.Details.PhotoLocation;

                            if (currentPhoto.Id == 0)
                            {
                                Database.Photos.Insert(currentPhoto);
                            }
                            else
                            {
                                Database.Photos.Update(currentPhoto);
                            }
                            FileDB db = FileDB.GetFileDB(region.FileDBName);
                            _ = WritePhotoToDb(currentPhoto, db); //Fire and forget

                        });
                    });
                });
            }
           
            skippedPhotoCount = skipped;
        }

        private static async Task WritePhotoToDb(SitePhoto currentPhoto, FileDB db)
        {
            var url = WikiSettings.UrlImages + currentPhoto.ThumbnailLocation;
            ByteBuffer buffer = Downloader.GetToBuffer(url);
            if (buffer != null)
            {
                var bytes = buffer.ToByteArray();
                await Task.Run(() => db.WriteFile(currentPhoto.File, bytes));
            }
        }
    }
}
