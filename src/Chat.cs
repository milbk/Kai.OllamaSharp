﻿using OllamaSharp.Models;
using OllamaSharp.Streamer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OllamaSharp.Models.Chat;
using System.Threading;
using Message = OllamaSharp.Models.Chat.Message;

namespace OllamaSharp
{
	public class Chat
	{
        public List<Message> Messages = new();

		public IOllamaApiClient Client { get; }

		public string Model { get; set; }

		public IResponseStreamer<ChatResponseStream> Streamer { get; }

		public Chat(IOllamaApiClient client, Action<ChatResponseStream> streamer)
			: this(client, new ActionResponseStreamer<ChatResponseStream>(streamer))
		{
		}

		public Chat(IOllamaApiClient client, IResponseStreamer<ChatResponseStream> streamer)
		{
			Client = client ?? throw new ArgumentNullException(nameof(client));
			Streamer = streamer ?? throw new ArgumentNullException(nameof(streamer));
		}

		/// <summary>
		/// Sends a message to the currently selected model
		/// </summary>
		/// <param name="message">The message to send</param>
		/// <param name="cancellationToken">The token to cancel the operation with</param>
		public Task<IEnumerable<Message>> Send(string message, CancellationToken cancellationToken = default) => Send(message, null, cancellationToken);

		/// <summary>
		/// Sends a message to the currently selected model
		/// </summary>
		/// <param name="message">The message to send</param>
		/// <param name="imagesAsBase64">Base64 encoded images to send to the model</param>
		/// <param name="cancellationToken">The token to cancel the operation with</param>
		public Task<IEnumerable<Message>> Send(string message, IEnumerable<string> imagesAsBase64, CancellationToken cancellationToken = default) => SendAs("user", message, imagesAsBase64, cancellationToken);

		/// <summary>
		/// Sends a message in a given role to the currently selected model
		/// </summary>
		/// <param name="role">The role in which the message should be sent</param>
		/// <param name="message">The message to send</param>
		/// <param name="cancellationToken">The token to cancel the operation with</param>
		public Task<IEnumerable<Message>> SendAs(ChatRole role, string message, CancellationToken cancellationToken = default) => SendAs(role, message, null, cancellationToken);

		/// <summary>
		/// Sends a message in a given role to the currently selected model
		/// </summary>
		/// <param name="role">The role in which the message should be sent</param>
		/// <param name="message">The message to send</param>
		/// <param name="imagesAsBase64">Base64 encoded images to send to the model</param>
		/// <param name="cancellationToken">The token to cancel the operation with</param>
		public async Task<IEnumerable<Message>> SendAs(ChatRole role, string message, IEnumerable<string> imagesAsBase64, CancellationToken cancellationToken = default)
		{
			Messages.Add(new Message(role, message, imagesAsBase64?.ToArray()));

			var request = new ChatRequest
			{
				Messages = Messages.ToList(),
				Model = Client.SelectedModel,
				Stream = true
			};

			var answer = await Client.SendChat(request, Streamer, cancellationToken);
            Messages = answer.ToList();
			return Messages;
		}
	}
}