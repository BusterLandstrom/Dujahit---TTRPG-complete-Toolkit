using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Dujahit.ViewModels;
using System;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Dujahit.Views
{
    public partial class ChatView : UserControl
    {
        private ChatViewModel? _vm;
        private INotifyCollectionChanged? _currentMessages;

        public ChatView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_vm != null)
                _vm.PropertyChanged -= OnVmPropertyChanged;

            _vm = DataContext as ChatViewModel;

            if (_vm != null)
            {
                _vm.PropertyChanged += OnVmPropertyChanged;
                HookMessages(_vm.Messages);
            }
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatViewModel.Messages) && _vm != null)
                HookMessages(_vm.Messages);
        }

        private void HookMessages(INotifyCollectionChanged? newCollection)
        {
            if (_currentMessages != null)
                _currentMessages.CollectionChanged -= OnMessagesChanged;

            _currentMessages = newCollection;

            if (_currentMessages != null)
                _currentMessages.CollectionChanged += OnMessagesChanged;

            ScrollToBottom();
        }

        private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
                ScrollToBottom();
        }

        private void ScrollToBottom()
        {
            Dispatcher.UIThread.Post(() =>
            {
                MessageScroller?.ScrollToEnd();
            }, DispatcherPriority.Background);
        }

        private void OnInputKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is ChatViewModel vm)
            {
                vm.SendMessageCommand.Execute().Subscribe();
                e.Handled = true;
            }
        }
    }
}