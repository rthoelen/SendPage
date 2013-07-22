using System;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using System.IO.IsolatedStorage;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using SendPage.Resources;

namespace SendPage
{
    public partial class MainPage : PhoneApplicationPage
    {

            IsolatedStorageSettings settings;

            const string snppserver = "snppserver";
            const string number = "number";

            // Cached Socket object that will be used by each call for the lifetime of this class
            Socket _socket = null;

            // Signaling object used to notify when an asynchronous operation is completed
            static ManualResetEvent _clientDone = new ManualResetEvent(false);

            // Define a timeout in milliseconds for each asynchronous call. If a response is not received within this 
            // timeout period, the call is aborted.
            const int TIMEOUT_MILLISECONDS = 5000;

            // The maximum size of the data buffer to use with the asynchronous socket methods
            const int MAX_BUFFER_SIZE = 2048;

        // Constructor
        public MainPage()
        {

            settings = IsolatedStorageSettings.ApplicationSettings;

            InitializeComponent();

            // Sample code to localize the ApplicationBar
            BuildLocalizedApplicationBar();

            // Set reasonable limits for SNPP

            this.numberTextBox.MaxLength = 10;
            this.messageTextBox.MaxLength = 240;

            if (settings.Contains("snppserver"))
            {
                snppTextBox.Text = (string)settings[snppserver];
            }

            if (settings.Contains("number"))
            {
                numberTextBox.Text = (string)settings[number];
            }


        }

        private void sendButton_Click(object sender, RoutedEventArgs e)
        {
            // First, save our settings for next time

            if (settings.Contains(snppserver))
            {
                settings[snppserver] = snppTextBox.Text;
            }
            else
            {
                settings.Add(snppserver, snppTextBox.Text);
            }

            if (settings.Contains(number))
            {
                settings[number] = numberTextBox.Text;
            }
            else
            {
                settings.Add(number, numberTextBox.Text);
            }
            
            // Check for empty server 

            if (snppTextBox.Text == "")
            {
                MessageBox.Show("Server name cannot be blank");
                return;
            }

            if (numberTextBox.Text == "")
            {
                MessageBox.Show("Pager number cannot be blank");
                return;
            }

            if (messageTextBox.Text == "")
            {
                MessageBox.Show("Please enter at least one character to send");
                return;
            }

            // Now connect to the server and send the message


            progress.Value = 0;

            string response;

            response = this.Connect(snppTextBox.Text, 444);

            if (response == "")
            {
                MessageBox.Show("Please check your internet connection and/or your server name");
                return;
            }
            progress.Value = 20;

            response = this.Receive();
            response = this.Send("PAGE " + numberTextBox.Text + "\n");
            response = this.Receive();

            if (!response.Contains("250"))
            {
                MessageBox.Show("Error:  Pager ID not accepted");
                this.CloseSocket();
                return;
            }

            response = this.Send("DATA\n");
            response = this.Receive();

            if (!response.Contains("354"))
            {
                MessageBox.Show("Error trying to initiate message transfer");
                this.CloseSocket();
                return;
            }

            progress.Value = 40;
            
            response = this.Send(messageTextBox.Text + "\n");
            response = this.Send(".\n");
            response = this.Receive();

            if (!response.Contains("250"))
            {
                MessageBox.Show("Error:  Message not accepted by network");
                this.CloseSocket();
                return;
            }

           
            response = this.Send("SEND\n");
            response = this.Receive();
            progress.Value = 60;

            if (!response.Contains("250"))
            {
                MessageBox.Show("Error:  Message could not be sent");
                this.CloseSocket();
                return;
            }

            Thread.Sleep(200);
            response = this.Send("QUIT\n");
            response = this.Receive();

            if (!response.Contains("221"))
            {
                MessageBox.Show("Unexpected response when sending QUIT command");
                this.CloseSocket();
                return;
            }

            progress.Value = 80;
            
            Thread.Sleep(200);
            this.CloseSocket();
            
            progress.Value = 100;

            MessageBox.Show("Page has been sent.");
        }

        // Sample code for building a localized ApplicationBar
        private void BuildLocalizedApplicationBar()
        {
        //    // Set the page's ApplicationBar to a new instance of ApplicationBar.
            ApplicationBar = new ApplicationBar();
            ApplicationBar.Mode = ApplicationBarMode.Minimized;

            ApplicationBarMenuItem appBarMenuItem = new ApplicationBarMenuItem("privacy policy");
            appBarMenuItem.Click += new EventHandler(privacy_click);
            ApplicationBar.MenuItems.Add(appBarMenuItem);

            ApplicationBarMenuItem appBarMenuItem1 = new ApplicationBarMenuItem("about");
            appBarMenuItem1.Click += new EventHandler(about_click);
            ApplicationBar.MenuItems.Add(appBarMenuItem1);
        }

        /// <summary>
        /// Attempt a TCP socket connection to the given host over the given port
        /// </summary>
        /// <param name="hostName">The name of the host</param>
        /// <param name="portNumber">The port number to connect</param>
        /// <returns>A string representing the result of this connection attempt</returns>
        public string Connect(string hostName, int portNumber)
        {
            string result = string.Empty;

            // Create DnsEndPoint. The hostName and port are passed in to this method.
            DnsEndPoint hostEntry = new DnsEndPoint(hostName, portNumber);

            // Create a stream-based, TCP socket using the InterNetwork Address Family. 
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // Create a SocketAsyncEventArgs object to be used in the connection request
            SocketAsyncEventArgs socketEventArg = new SocketAsyncEventArgs();
            socketEventArg.RemoteEndPoint = hostEntry;

            // Inline event handler for the Completed event.
            // Note: This event handler was implemented inline in order to make this method self-contained.
            socketEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(delegate(object s, SocketAsyncEventArgs e)
            {
                // Retrieve the result of this request
                result = e.SocketError.ToString();

                // Signal that the request is complete, unblocking the UI thread
                _clientDone.Set();
            });

            // Sets the state of the event to nonsignaled, causing threads to block
            _clientDone.Reset();

            // Make an asynchronous Connect request over the socket
            _socket.ConnectAsync(socketEventArg);

            // Block the UI thread for a maximum of TIMEOUT_MILLISECONDS milliseconds.
            // If no response comes back within this time then proceed
            _clientDone.WaitOne(TIMEOUT_MILLISECONDS);

            return result;
        }

        /// <summary>
        /// Send the given data to the server using the established connection
        /// </summary>
        /// <param name="data">The data to send to the server</param>
        /// <returns>The result of the Send request</returns>
        public string Send(string data)
        {
            string response = "Operation Timeout";

            // We are re-using the _socket object initialized in the Connect method
            if (_socket != null)
            {
                // Create SocketAsyncEventArgs context object
                SocketAsyncEventArgs socketEventArg = new SocketAsyncEventArgs();

                // Set properties on context object
                socketEventArg.RemoteEndPoint = _socket.RemoteEndPoint;
                socketEventArg.UserToken = null;

                // Inline event handler for the Completed event.
                // Note: This event handler was implemented inline in order 
                // to make this method self-contained.
                socketEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(delegate(object s, SocketAsyncEventArgs e)
                {
                    response = e.SocketError.ToString();

                    // Unblock the UI thread
                    _clientDone.Set();
                });

                // Add the data to be sent into the buffer
                byte[] payload = Encoding.UTF8.GetBytes(data);
                socketEventArg.SetBuffer(payload, 0, payload.Length);

                // Sets the state of the event to nonsignaled, causing threads to block
                _clientDone.Reset();

                // Make an asynchronous Send request over the socket
                _socket.SendAsync(socketEventArg);

                // Block the UI thread for a maximum of TIMEOUT_MILLISECONDS milliseconds.
                // If no response comes back within this time then proceed
                _clientDone.WaitOne(TIMEOUT_MILLISECONDS);
            }
            else
            {
                response = "Socket is not initialized";
            }

            return response;
        }

        /// <summary>
        /// Receive data from the server using the established socket connection
        /// </summary>
        /// <returns>The data received from the server</returns>
        public string Receive()
        {
            string response = "Operation Timeout";

            // We are receiving over an established socket connection
            if (_socket != null)
            {
                // Create SocketAsyncEventArgs context object
                SocketAsyncEventArgs socketEventArg = new SocketAsyncEventArgs();
                socketEventArg.RemoteEndPoint = _socket.RemoteEndPoint;

                // Setup the buffer to receive the data
                socketEventArg.SetBuffer(new Byte[MAX_BUFFER_SIZE], 0, MAX_BUFFER_SIZE);

                // Inline event handler for the Completed event.
                // Note: This even handler was implemented inline in order to make 
                // this method self-contained.
                socketEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(delegate(object s, SocketAsyncEventArgs e)
                {
                    if (e.SocketError == SocketError.Success)
                    {
                        // Retrieve the data from the buffer
                        response = Encoding.UTF8.GetString(e.Buffer, e.Offset, e.BytesTransferred);
                        response = response.Trim('\0');
                    }
                    else
                    {
                        response = e.SocketError.ToString();
                    }

                    _clientDone.Set();
                });

                // Sets the state of the event to nonsignaled, causing threads to block
                _clientDone.Reset();

                // Make an asynchronous Receive request over the socket
                _socket.ReceiveAsync(socketEventArg);

                // Block the UI thread for a maximum of TIMEOUT_MILLISECONDS milliseconds.
                // If no response comes back within this time then proceed
                _clientDone.WaitOne(TIMEOUT_MILLISECONDS);
            }
            else
            {
                response = "Socket is not initialized";
            }

            return response;
        }

        /// <summary>
        /// Closes the Socket connection and releases all associated resources
        /// </summary>
        public void CloseSocket()
        {
            if (_socket != null)
            {
                _socket.Close();
            }
        }

        private void about_click(object sender, EventArgs e)
        {
            MessageBox.Show("This is SendPage, desinged to send messages to pagers.  App created by RTCubed Consulting");
        }

        private void privacy_click(object sender, EventArgs e)
        {
            Windows.System.Launcher.LaunchUriAsync(new Uri("http://www.rtcubed.com/sendpage-app"));
        }
    }
}