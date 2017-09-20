#include "pch.h"
#include "EosConsole.h"
#include <time.h>
#include <string>
#include <iostream>
#include <mutex>
#include <collection.h>

using namespace Eos;
using namespace Concurrency;
using namespace Platform;

EosConsole::EosConsole()
{
	
}

#pragma region Public Methods

bool EosConsole::Connect(Platform::String^ ipAddress)
{
	OutputDebugString(L"Connecting...\n");
	Disconnect();
	m_ConsoleIP = ipAddress;
	if (m_ConsoleIP == nullptr) return false;
	std::string ip = EosConsole::Utf8_Encode(std::wstring(ipAddress->Data()));
	try
	{
		m_Mutex.lock();
		if (m_EosSyncLib.Initialize(ip.c_str(), EosSyncLib::DEFAULT_PORT))
		{
			m_Mutex.unlock();

			m_Run = true;
			//Start worker thread
			auto t = create_task([&]()
			{
				//OutputDebugString(L"Task Started\n");
				
				while (m_Run)
				{
					//OutputDebugString(L"Running worker loop\n");

					m_Mutex.lock();					
					m_EosSyncLib.Tick();

					if (m_EosSyncLib.IsConnected())
					{
						//OutputDebugString(L"Eos Sync lib is connected\n");
						m_EosSyncLib.ClearDirty();
					}						
					
					m_Mutex.unlock();
					EosTimer::SleepMS(10);
					//OutputDebugString(L"Exiting worker loop\n");
				}
			});
			return true;
		}
		
	}
	catch (const std::string& ex)
	{

	}
	m_Mutex.unlock();
	return false;
}

void EosConsole::Disconnect()
{
	try
	{
		m_Run = false;
		//Shut down worker thread
		m_Mutex.lock();
		m_EosSyncLib.Shutdown();
		m_Mutex.unlock();
	}
	catch (const std::string& ex)
	{
		m_Mutex.unlock();
	}
}

bool EosConsole::IsRunning()
{
	bool status = LockEosSyncLib()->IsRunning();
	UnlockEosSyncLib();
	return status;
}

bool EosConsole::IsConnected()
{
	EosSyncLib* eosSyncLib = LockEosSyncLib();
	bool connected = eosSyncLib->IsConnected();
	UnlockEosSyncLib();
	return connected;
}

bool EosConsole::IsSynced()
{
	EosSyncLib* eosSyncLib = LockEosSyncLib();
	bool syncd = eosSyncLib->GetData().GetStatus().GetValue() == EosSyncStatus::SYNC_STATUS_COMPLETE;
	UnlockEosSyncLib();
	return syncd;
}

void EosConsole::SendOscString(Platform::String^ str)
{
	OSCPacketWriter *packet = OSCPacketWriter::CreatePacketWriterForString(Utf8_Encode(str->Data()).c_str());
	if (packet)
	{
		m_Mutex.lock();
		m_EosSyncLib.Send(*packet, /*immediate*/true);
		m_Mutex.unlock();

		delete packet;
	}
}

#pragma endregion Public Methods

Windows::Foundation::Collections::IVector<EosPatch^>^ EosConsole::GetPatchInfo()
{
	Platform::Collections::Vector<EosPatch^>^ patchData = ref new Platform::Collections::Vector<EosPatch^>();
	const EosSyncData::SHOW_DATA &showData = LockEosSyncLib()->GetData().GetShowData();

	for (EosSyncData::SHOW_DATA::const_iterator i = showData.begin(); i != showData.end(); i++)
	{
		EosTarget::EnumEosTargetType targetType = i->first;

		if (targetType == EosTarget::EnumEosTargetType::EOS_TARGET_PATCH)
		{
			const EosSyncData::TARGETLIST_DATA &listData = i->second;
			for (EosSyncData::TARGETLIST_DATA::const_iterator j = listData.begin(); j != listData.end(); j++)
			{
				int listNumber = j->first;
				const EosTargetList *targetList = j->second;
				const EosTargetList::TARGETS &targets = targetList->GetTargets();

				//For each channel
				for (EosTargetList::TARGETS::const_iterator j = targets.begin(); j != targets.end(); j++)
				{
					const EosTarget::sDecimalNumber &targetNumber = j->first;
					const EosTargetList::PARTS &parts = j->second.list;

					//For each part
					for (EosTargetList::PARTS::const_iterator k = parts.begin(); k != parts.end(); k++)
					{
						int partNumber = k->first;
						const EosTarget *target = k->second;
						const EosTarget::PROP_GROUPS &propGroups = target->GetPropGroups();

						std::string channel;
						EosTarget::GetStringFromNumber(targetNumber, channel);
						std::string part = std::to_string(partNumber);

						std::string type;
						std::string universe;
						std::string address;

						//for all channel properties
						for (EosTarget::PROP_GROUPS::const_iterator k = propGroups.begin(); k != propGroups.end(); k++)
						{
							const std::string &propGroupName = k->first;
							const EosTarget::PROPS &props = k->second.props;

							bool subGroup = !propGroupName.empty();						
							if (!subGroup)
							{
								int i = 0;
								for (EosTarget::PROPS::const_iterator l = props.begin(); l != props.end(); l++)
								{
									switch (i)
									{
										case 4 :
											type = l->value;
											break;
										case 19 :
											universe = l->value;
											break;
										case 20:
											address = l->value;
											break;
									}
									i++;
								}
							}							
						}

						EosPatch^ patch = ref new EosPatch(ref new Platform::String(Utf8_Decode(channel).c_str()),
							ref new Platform::String(Utf8_Decode(part).c_str()), ref new Platform::String(Utf8_Decode(type).c_str()),
							ref new Platform::String(Utf8_Decode(universe).c_str()), ref new Platform::String(Utf8_Decode(address).c_str()));
						patchData->Append(patch);
					}
				}
			}
		}		
	}

	UnlockEosSyncLib();
	return patchData;
}

Platform::String^ EosConsole::GetCmdLine()
{
	const EosSyncData &syncData = LockEosSyncLib()->GetData();
	auto output = StdStrDecode(syncData.GetCmdLine());
	UnlockEosSyncLib();
	return output;
}


#pragma region Helper Methods

EosSyncLib* EosConsole::LockEosSyncLib()
{
	m_Mutex.lock();
	return &m_EosSyncLib;
}

void EosConsole::UnlockEosSyncLib()
{
	m_Mutex.unlock();
}

std::string EosConsole::Utf8_Encode(const std::wstring & wstr)
{
	if (wstr.empty()) return std::string();
	int size_needed = WideCharToMultiByte(CP_UTF8, 0, &wstr[0], (int)wstr.size(), NULL, 0, NULL, NULL);
	std::string strTo(size_needed, 0);
	WideCharToMultiByte(CP_UTF8, 0, &wstr[0], (int)wstr.size(), &strTo[0], size_needed, NULL, NULL);
	return strTo;
}

std::wstring EosConsole::Utf8_Decode(const std::string &str)
{
	if (str.empty()) return std::wstring();
	int size_needed = MultiByteToWideChar(CP_UTF8, 0, &str[0], (int)str.size(), NULL, 0);
	std::wstring wstrTo(size_needed, 0);
	MultiByteToWideChar(CP_UTF8, 0, &str[0], (int)str.size(), &wstrTo[0], size_needed);
	return wstrTo;
}

Platform::String^ EosConsole::StdStrDecode(const std::string &str)
{
	return ref new Platform::String(Utf8_Decode(str).c_str());
}

#pragma endregion Helper Methods