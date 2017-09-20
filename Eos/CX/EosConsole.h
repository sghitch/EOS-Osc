#pragma once

namespace Eos 
{
	[Windows::Foundation::Metadata::WebHostHidden]
	public ref class EosConsole sealed
	{
		
	public:
		EosConsole();
		//Accessors
		Windows::Foundation::Collections::IVector<EosPatch^>^ GetPatchInfo();
		Platform::String^ GetCmdLine();
		//Methods
		bool Connect(Platform::String^ ConsoleIP);
		void Disconnect();
		bool IsRunning();
		bool IsConnected();
		bool IsSynced();
		void SendOscString(Platform::String^ str);

	private:
		//Member Variables
		Platform::String^ m_ConsoleIP;
		EosSyncLib m_EosSyncLib;
		std::mutex m_Mutex;
		bool m_Run;

		//Methods
		EosSyncLib* LockEosSyncLib();
		void UnlockEosSyncLib();
		std::string Utf8_Encode(const std::wstring &wstr);
		std::wstring Utf8_Decode(const std::string &str);
		Platform::String^ StdStrDecode(const std::string &str);
	};
}
