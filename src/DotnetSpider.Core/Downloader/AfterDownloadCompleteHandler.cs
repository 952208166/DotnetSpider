﻿using System;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using NLog;
using DotnetSpider.Core.Infrastructure;
using DotnetSpider.Core.Redial;
using System.Linq;

namespace DotnetSpider.Core.Downloader
{
	public abstract class AfterDownloadCompleteHandler : Named, IAfterDownloadCompleteHandler
	{
		protected static readonly ILogger Logger = LogCenter.GetLogger();

		public abstract void Handle(ref Page page, ISpider spider);
	}

	public class UpdateCookieWhenContainsContentHandler : AfterDownloadCompleteHandler
	{
		public ICookieInjector CookieInjector { get; set; }

		public string Content { get; set; }

		public override void Handle(ref Page page, ISpider spider)
		{
			if (page != null && !string.IsNullOrEmpty(page.Content) && page.Content.Contains(Content))
			{
				CookieInjector?.Inject(spider);
			}
			throw new SpiderException($"Content downloaded contains string: {Content}.");
		}
	}

	public class UpdateCookieTimerHandler : AfterDownloadCompleteHandler
	{
		public ICookieInjector CookieInjector { get; }

		public int DueTime { get; }

		public DateTime NextTime { get; private set; }

		public UpdateCookieTimerHandler(int dueTime, ICookieInjector injector)
		{
			DueTime = dueTime;
			CookieInjector = injector;
			NextTime = DateTime.Now.AddSeconds(DueTime);
		}

		public override void Handle(ref Page page, ISpider spider)
		{
			if (DateTime.Now > NextTime)
			{
				CookieInjector?.Inject(spider);
				NextTime = DateTime.Now.AddSeconds(DueTime);
			}
		}
	}

	public class SkipWhenContainsContentHandler : AfterDownloadCompleteHandler
	{
		public string Content { get; set; }

		public override void Handle(ref Page page, ISpider spider)
		{
			page.Skip = !string.IsNullOrEmpty(page?.Content) && page.Content.Contains(Content);
		}
	}

	public class SkipTargetUrlsWhenNotContainsContentHandler : AfterDownloadCompleteHandler
	{
		public string Content { get; set; }

		public override void Handle(ref Page page, ISpider spider)
		{
			if (page != null && !string.IsNullOrEmpty(page.Content) && !page.Content.Contains(Content))
			{
				page.SkipExtractTargetUrls = true;
				page.SkipTargetUrls = true;
			}
		}
	}

	public class RemoveHtmlTagHandler : AfterDownloadCompleteHandler
	{
		public override void Handle(ref Page page, ISpider spider)
		{
			if (page != null && !string.IsNullOrEmpty(page.Content))
			{
				var htmlDocument = new HtmlDocument();
				htmlDocument.LoadHtml(page.Content);
				page.Content = htmlDocument.DocumentNode.InnerText;
			}
		}
	}

	public class ContentToUpperHandler : AfterDownloadCompleteHandler
	{
		public override void Handle(ref Page page, ISpider spider)
		{
			if (page != null && !string.IsNullOrEmpty(page.Content))
			{
				page.Content = page.Content.ToUpper();
			}
		}
	}

	public class ContentToLowerHandler : AfterDownloadCompleteHandler
	{
		public override void Handle(ref Page page, ISpider spider)
		{
			if (page != null && !string.IsNullOrEmpty(page.Content))
			{
				page.Content = page.Content.ToLower();
			}
		}
	}

	public class ReplaceContentHandler : AfterDownloadCompleteHandler
	{
		public string OldValue { get; set; }

		public string NewValue { get; set; }

		public override void Handle(ref Page page, ISpider spider)
		{
			if (page != null && !string.IsNullOrEmpty(page.Content))
			{
				page.Content = page.Content.Replace(OldValue, NewValue);
			}
		}
	}

	public class TrimContentHandler : AfterDownloadCompleteHandler
	{
		public override void Handle(ref Page page, ISpider spider)
		{
			if (page != null && !string.IsNullOrEmpty(page.Content))
			{
				page.Content = page.Content.Trim();
			}
		}
	}

	public class UnescapeContentHandler : AfterDownloadCompleteHandler
	{
		public override void Handle(ref Page page, ISpider spider)
		{
			if (page != null && !string.IsNullOrEmpty(page.Content))
			{
				page.Content = Regex.Unescape(page.Content);
			}
		}
	}

	public class PatternMatchContentHandler : AfterDownloadCompleteHandler
	{
		public string Pattern { get; set; }

		public override void Handle(ref Page page, ISpider spider)
		{
			if (page == null || string.IsNullOrEmpty(page.Content))
			{
				return;
			}

			string textValue = string.Empty;
			MatchCollection collection = Regex.Matches(page.Content, Pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);

			foreach (Match item in collection)
			{
				textValue += item.Value;
			}
			page.Content = textValue;
		}
	}

	public class RetryWhenContainsContentHandler : AfterDownloadCompleteHandler
	{
		public string[] Contents { get; set; }

		public RetryWhenContainsContentHandler(params string[] contents)
		{
			if (contents == null || contents.Length == 0)
			{
				throw new SpiderException("Contents should not be empty/null.");
			}
			Contents = contents;
		}

		public override void Handle(ref Page page, ISpider spider)
		{
			if (page != null && !string.IsNullOrEmpty(page.Content))
			{
				var tmpPage = page;
				if (Contents.Any(c => tmpPage.Content.Contains(c)))
				{
					Request r = page.Request.Clone();
					page.AddTargetRequest(r);
				}
			}
		}
	}

	public class RedialWhenContainsContentHandler : AfterDownloadCompleteHandler
	{
		public string Content { get; set; }

		public override void Handle(ref Page page, ISpider spider)
		{
			if (page != null && !string.IsNullOrEmpty(page.Content) && !string.IsNullOrEmpty(Content) && page.Content.Contains(Content))
			{
				if (NetworkCenter.Current.Executor.Redial() == RedialResult.Failed)
				{
					Logger.MyLog(spider.Identity, "Exit program because redial failed.", LogLevel.Error);
					spider.Exit();
				}
				page = Spider.AddToCycleRetry(page.Request, spider.Site);
				page.Exception = new DownloadException($"Content downloaded contains string: {Content}.");
			}
		}
	}

	public class RedialWhenExceptionThrowHandler : AfterDownloadCompleteHandler
	{
		public string ExceptionMessage { get; set; } = string.Empty;

		public override void Handle(ref Page page, ISpider spider)
		{
			if (page != null && !string.IsNullOrEmpty(page.Content) && !string.IsNullOrEmpty(ExceptionMessage) && page.Exception != null)
			{
				if (string.IsNullOrEmpty(ExceptionMessage))
				{
					page.Exception = new SpiderException("ExceptionMessage should not be empty/null.");
				}
				if (page.Exception.Message.Contains(ExceptionMessage))
				{
					if (NetworkCenter.Current.Executor.Redial() == RedialResult.Failed)
					{
						Logger.MyLog(spider.Identity, "Exit program because redial failed.", LogLevel.Error);
						spider.Exit();
					}
					Spider.AddToCycleRetry(page.Request, spider.Site);
					page.Exception = new DownloadException("Download failed and redial finished already.");
				}
			}
		}
	}

	public class RedialAndUpdateCookieWhenContainsContentHandler : AfterDownloadCompleteHandler
	{
		public string Content { get; set; }

		public ICookieInjector CookieInjector { get; set; }

		public override void Handle(ref Page page, ISpider spider)
		{
			if (page != null && !string.IsNullOrEmpty(page.Content) && !string.IsNullOrEmpty(Content) && CookieInjector != null && page.Content.Contains(Content))
			{
				if (NetworkCenter.Current.Executor.Redial() == RedialResult.Failed)
				{
					spider.Exit();
				}
				Spider.AddToCycleRetry(page.Request, spider.Site);
				CookieInjector?.Inject(spider);
				page.Exception = new DownloadException($"Content downloaded contains string: {Content}.");
			}
		}
	}

	public class CycleRedialHandler : AfterDownloadCompleteHandler
	{
		private readonly object _locker = new object();

		public int RedialLimit { get; set; }

		public static int RequestedCount { get; set; }

		public override void Handle(ref Page page, ISpider spider)
		{
			if (RedialLimit != 0)
			{
				lock (_locker)
				{
					++RequestedCount;

					if (RedialLimit > 0 && RequestedCount == RedialLimit)
					{
						RequestedCount = 0;
						Spider.AddToCycleRetry(page.Request, spider.Site);
						if (NetworkCenter.Current.Executor.Redial() == RedialResult.Failed)
						{
							spider.Exit();
						}
					}
				}
			}
		}
	}

	public class SubContentHandler : AfterDownloadCompleteHandler
	{
		public string StartPart { get; set; }
		public string EndPart { get; set; }
		public int StartOffset { get; set; } = 0;
		public int EndOffset { get; set; } = 0;

		public override void Handle(ref Page page, ISpider spider)
		{
			if (page == null || string.IsNullOrEmpty(page.Content))
			{
				return;
			}

			string rawText = page.Content;

			int begin = rawText.IndexOf(StartPart, StringComparison.Ordinal);
			int end = rawText.IndexOf(EndPart, begin, StringComparison.Ordinal);
			int length = end - begin;

			begin += StartOffset;
			length -= StartOffset;
			length -= EndOffset;
			length += EndPart.Length;

			if (begin < 0 || length < 0)
			{
				throw new SpiderException("Sub content failed. Please check your settings.");
			}
			string newRawText = rawText.Substring(begin, length).Trim();
			page.Content = newRawText;
		}
	}
}